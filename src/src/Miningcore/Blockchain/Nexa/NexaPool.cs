using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using NLog;
using static Miningcore.Util.ActionUtils;
using Miningcore.Blockchain.Bitcoin;

namespace Miningcore.Blockchain.Nexa;

[CoinFamily(CoinFamily.Nexa)]
public class NexaPool : PoolBase
{
    public NexaPool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
    }

    private object currentJobParams;
    private NexaJobManager manager;
    private BitcoinTemplate coin;

    protected virtual async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var context = connection.ContextAs<NexaWorkerContext>();
        var requestParams = request.ParamsAs<string[]>();

        var data = new object[]
        {
            new object[]
            {
                new object[] { BitcoinStratumMethods.SetDifficulty, connection.ConnectionId },
                new object[] { BitcoinStratumMethods.MiningNotify, connection.ConnectionId }
            }
        }
        .Concat(manager.GetSubscriberData(connection))
        .ToArray();

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        // [Respect the goddamn standards Nicehack :(]
        var response = new JsonRpcResponse<object[]>(data, request.Id);

        if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        await connection.RespondAsync(response);

        // setup worker context
        context.IsSubscribed = true;
        context.UserAgent = requestParams.FirstOrDefault()?.Trim();

        // Nicehash support
        var nicehashDiff = await GetNicehashStaticMinDiff(context, coin.Name, coin.GetAlgorithmName());

        if(nicehashDiff.HasValue)
        {
            logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

            context.VarDiff = null; // disable vardiff
            context.SetDifficulty(nicehashDiff.Value);
        }

        var minerJobParams = CreateWorkerJob(connection, context.IsSubscribed);

        // send intial update
        await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
        await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, minerJobParams);
    }

    protected virtual async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var context = connection.ContextAs<NexaWorkerContext>();
        var requestParams = request.ParamsAs<string[]>();
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var split = workerValue?.Split('.');
        var minerName = split?.FirstOrDefault()?.Trim();
        var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

        // assumes that minerName is an address
        context.IsAuthorized = await manager.ValidateAddressAsync(minerName, ct);
        context.Miner = minerName;
        context.Worker = workerName;

        if(context.IsAuthorized)
        {
            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(context.IsAuthorized, request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            // respond
            await connection.RespondAsync(response);

            // log association
            logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");

                await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            }
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                // issue short-time ban if unauthorized to prevent DDos on daemon (validateaddress RPC)
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private object CreateWorkerJob(StratumConnection connection, bool cleanJob)
    {
        var context = connection.ContextAs<NexaWorkerContext>();
        var job = manager.GetJobForStratum();

        // update context
        lock(context)
        {
            context.AddJob(job, manager.maxActiveJobs);
        }

        return job.GetJobParams();
    }

    protected virtual async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<NexaWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            // check worker state
            context.LastActivity = clock.Now;

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
            else if(!context.IsSubscribed)
                throw new StratumException(StratumError.NotSubscribed, "not subscribed");

            var requestParams = request.ParamsAs<string[]>();

            // submit
            var share = await manager.SubmitShareAsync(connection, requestParams, ct);

            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(true, request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            await connection.RespondAsync(response);

            // publish
            messageBus.SendMessage(share);

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * coin.ShareMultiplier, 3)}");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    private async Task OnSuggestDifficultyAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<NexaWorkerContext>();

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        // [Respect the goddamn standards Nicehack :(]
        var response = new JsonRpcResponse<object>(true, request.Id);

        if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        // acknowledge
        await connection.RespondAsync(response);

        try
        {
            var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double)!;

            // client may suggest higher-than-base difficulty, but not a lower one
            var poolEndpoint = poolConfig.Ports[connection.LocalEndpoint.Port];

            if(requestedDiff > poolEndpoint.Difficulty)
            {
                context.SetDifficulty(requestedDiff);
                await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                logger.Info(() => $"[{connection.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
            }
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Unable to convert suggested difficulty {request.Params}");
        }
    }

    protected virtual async Task OnNewJobAsync(object jobParams)
    {
        currentJobParams = jobParams;

        logger.Info(() => $"Broadcasting job {((object[]) jobParams)[0]}");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            var context = connection.ContextAs<NexaWorkerContext>();
            var minerJobParams = CreateWorkerJob(connection, true);

            // varDiff: if the client has a pending difficulty change, apply it now
            if(context.ApplyPendingDifficulty())
                await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

            // send job
            await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, minerJobParams);
        }));
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        var multiplier = BitcoinConstants.Pow2x32;
        var result = shares * multiplier / interval;

        if(coin.HashrateMultiplier.HasValue)
            result *= coin.HashrateMultiplier.Value;

        return result;
    }

    public override double ShareMultiplier => coin.ShareMultiplier;

    #region Overrides

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<BitcoinTemplate>();

        base.Configure(pc, cc);
    }

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<NexaJobManager>(
            new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        if(poolConfig.EnableInternalStratum == true)
        {
            disposables.Add(manager.Jobs
                .Select(job => Observable.FromAsync(() =>
                    Guard(() => OnNewJobAsync(job),
                        ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(OnNewJobAsync));
                }));

            // start with initial blocktemplate
            await manager.Jobs.Take(1).ToTask(ct);
        }

        else
        {
            // keep updating NetworkStats
            disposables.Add(manager.Jobs.Subscribe());
        }
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new NexaWorkerContext();
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;

        try
        {
            switch(request.Method)
            {
                case BitcoinStratumMethods.Subscribe:
                    await OnSubscribeAsync(connection, tsRequest);
                    break;

                case BitcoinStratumMethods.Authorize:
                    await OnAuthorizeAsync(connection, tsRequest, ct);
                    break;

                case BitcoinStratumMethods.SubmitShare:
                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case BitcoinStratumMethods.SuggestDifficulty:
                    await OnSuggestDifficultyAsync(connection, tsRequest);
                    break;

                case BitcoinStratumMethods.ExtraNonceSubscribe:
                    var context = connection.ContextAs<NexaWorkerContext>();

                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [Respect the goddamn standards Nicehack :(]
                    var response = new JsonRpcResponse<object>(true, request.Id);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        response.Extra = new Dictionary<string, object>();
                        response.Extra["error"] = null;
                    }

                    await connection.RespondAsync(response);
                    break;

                default:
                    logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        if(connection.Context.ApplyPendingDifficulty())
        {
            var minerJobParams = CreateWorkerJob(connection, true);

            await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { connection.Context.Difficulty });
            await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, minerJobParams);
        }
    }

    #endregion // Overrides
}
