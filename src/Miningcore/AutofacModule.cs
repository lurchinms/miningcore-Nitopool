using System.Reflection;
using Autofac;
using Miningcore.Api;
using Miningcore.Banning;
using Miningcore.Blockchain.Alephium;
using Miningcore.Blockchain.Beam;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Conceal;
using Miningcore.Blockchain.Cryptonote;
using Miningcore.Blockchain.Equihash;
using Miningcore.Blockchain.Ergo;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Blockchain.Handshake;
using Miningcore.Blockchain.Kaspa;
using Miningcore.Blockchain.Nexa;
using Miningcore.Blockchain.Progpow;
using Miningcore.Blockchain.Warthog;
using Miningcore.Blockchain.Xelis;
using Miningcore.Blockchain.Zano;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Crypto.Hashing.Progpow;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.Payments.PaymentSchemes;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = Autofac.Module;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using Miningcore.Nicehash;
using Miningcore.Pushover;
using Miningcore.CoinMarketCap;

namespace Miningcore;

public class AutofacModule : Module
{
    /// <summary>
    /// Override to add registrations to the container.
    /// </summary>
    /// <remarks>
    /// Note that the ContainerBuilder parameter is unique to this module.
    /// </remarks>
    /// <param name="builder">The builder through which components can be registered.</param>
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false
                }
            }
        });

        builder.RegisterType<MessageBus>()
            .AsImplementedInterfaces()
            .SingleInstance();

        builder.RegisterInstance(new RecyclableMemoryStreamManager
        (
            new RecyclableMemoryStreamManager.Options
            {
                ThrowExceptionOnToArray = true
            }
        ));

        builder.RegisterType<StandardClock>()
            .AsImplementedInterfaces()
            .SingleInstance();

        builder.RegisterType<IntegratedBanManager>()
            .Keyed<IBanManager>(BanManagerKind.Integrated)
            .SingleInstance();

        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.GetCustomAttributes<CoinFamilyAttribute>().Any() && t.GetInterfaces()
                .Any(i =>
                    i.IsAssignableFrom(typeof(IMiningPool)) ||
                    i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                    i.IsAssignableFrom(typeof(IPayoutScheme))))
            .WithMetadataFrom<CoinFamilyAttribute>()
            .AsImplementedInterfaces();

        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.GetCustomAttributes<IdentifierAttribute>().Any() &&
                t.GetInterfaces().Any(i => i.IsAssignableFrom(typeof(IHashAlgorithm))))
            .Named<IHashAlgorithm>(t=> t.GetCustomAttributes<IdentifierAttribute>().First().Name)
            .PropertiesAutowired();
        
        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.GetCustomAttributes<IdentifierAttribute>().Any() &&
                t.GetInterfaces().Any(i => i.IsAssignableFrom(typeof(IEthashLight))))
            .Named<IEthashLight>(t => t.GetCustomAttributes<IdentifierAttribute>().First().Name)
            .PropertiesAutowired();

        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.GetCustomAttributes<IdentifierAttribute>().Any() &&
                t.GetInterfaces().Any(i => i.IsAssignableFrom(typeof(IProgpowLight))))
            .Named<IProgpowLight>(t => t.GetCustomAttributes<IdentifierAttribute>().First().Name)
            .PropertiesAutowired();

        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.IsAssignableTo<EquihashSolver>())
            .PropertiesAutowired()
            .AsSelf();

        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t => t.IsAssignableTo<ControllerBase>())
            .PropertiesAutowired()
            .AsSelf();

        builder.RegisterType<WebSocketNotificationsRelay>()
            .PropertiesAutowired()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<NicehashService>()
            .SingleInstance();

        builder.RegisterType<CoinMarketCapService>()
            .SingleInstance();

        builder.RegisterType<PushoverClient>()
            .SingleInstance();

        //////////////////////
        // Background services

        builder.RegisterType<PayoutManager>()
            .SingleInstance();

        builder.RegisterType<ShareRecorder>()
            .SingleInstance();

        builder.RegisterType<ShareReceiver>()
            .SingleInstance();

        builder.RegisterType<BtStreamReceiver>()
            .SingleInstance();

        builder.RegisterType<ShareRelay>()
            .SingleInstance();

        builder.RegisterType<StatsRecorder>()
            .SingleInstance();

        builder.RegisterType<NotificationService>()
            .SingleInstance();

        builder.RegisterType<MetricsPublisher>()
            .SingleInstance();

        //////////////////////
        // Payment Schemes

        builder.RegisterType<PPLNSPaymentScheme>()
            .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
            .SingleInstance();

        builder.RegisterType<PPLNSBFPaymentScheme>()
            .Keyed<IPayoutScheme>(PayoutScheme.PPLNSBF)
            .SingleInstance();

        builder.RegisterType<SOLOPaymentScheme>()
            .Keyed<IPayoutScheme>(PayoutScheme.SOLO)
            .SingleInstance();

        builder.RegisterType<PROPPaymentScheme>()
            .Keyed<IPayoutScheme>(PayoutScheme.PROP)
            .SingleInstance();
        
        //////////////////////
        // Alephium

        builder.RegisterType<AlephiumJobManager>();
        
        //////////////////////
        // Beam

        builder.RegisterType<BeamJobManager>();
        
        //////////////////////
        // Bitcoin and family

        builder.RegisterType<BitcoinJobManager>();

        //////////////////////
        // Conceal

        builder.RegisterType<ConcealJobManager>();
        
        //////////////////////
        // Cryptonote

        builder.RegisterType<CryptonoteJobManager>();

        //////////////////////
        // ZCash

        builder.RegisterType<EquihashJobManager>();

        //////////////////////
        // Ergo

        builder.RegisterType<ErgoJobManager>();

        //////////////////////
        // Ethereum

        builder.RegisterType<EthereumJobManager>();

        //////////////////////
        // Handshake

        builder.RegisterType<HandshakeJobManager>();
        
        //////////////////////
        // Kaspa

        builder.RegisterType<KaspaJobManager>();

        //////////////////////
        // Nexa

        builder.RegisterType<NexaJobManager>();
        
        //////////////////////
        // Progpow

        builder.RegisterType<ProgpowJobManager>();

        //////////////////////
        // Warthog

        builder.RegisterType<WarthogJobManager>();

        //////////////////////
        // Xelis

        builder.RegisterType<XelisJobManager>();

        //////////////////////
        // Zano

        builder.RegisterType<ZanoJobManager>();

        base.Load(builder);
    }
}
