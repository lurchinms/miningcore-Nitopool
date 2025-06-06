CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable $(CPU_FLAGS) $(HAVE_FEATURE)
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11 $(CPU_FLAGS) $(HAVE_FEATURE)
LDFLAGS = -shared
LDLIBS = -lsodium
TARGET  = libmultihash.so

OBJECTS = bcrypt.o blake.o c11.o dcrypt.o fresh.o lane.o \
	fugue.o groestl.o hefty1.o jh.o keccak.o neoscrypt.o exports.o nist5.o quark.o qubit.o s3.o scryptn.o \
	sha256csm.o hmq17.o phi.o phi2.o \
	sha3/aes_helper.o sha3/hamsi.o sha3/hamsi_helper.o sha3/sph_blake.o sha3/sph_bmw.o sha3/sph_cubehash.o \
	sha3/sph_echo.o sha3/sph_fugue.o sha3/sph_groestl.o sha3/sph_hefty1.o sha3/sph_jh.o sha3/sph_keccak.o \
	sha3/sph_luffa.o sha3/sph_shabal.o sha3/sph_shavite.o sha3/sph_simd.o sha3/sph_skein.o sha3/sph_whirlpool.o \
	sha3/sph_haval.o sha3/sph_sha2.o sha3/sph_sha2big.o sha3/sm3.o sha3/panama.o \
	sha3/extra.o sha3/gost_streebog.o sha3/sph_tiger.o sha3/SWIFFTX.o KeccakP-800-reference.o \
        shake/cshake.o shake/keccak.o shake/shake.o \
	shavite3.o skein.o skein2.o x11.o x13.o x15.o x17.o x16r.o x16rv2.o x16s.o x21s.o x22i.o \
	blake2/sse/blake2s.o blake2/sse/blake2b.o \
        blake3/blake3.o blake3/blake3_dispatch.o blake3/blake3_portable.o \
        blake3/blake3_sse2_x86-64_unix.o blake3/blake3_sse41_x86-64_unix.o blake3/blake3_avx2_x86-64_unix.o \
        blake3/blake3_avx512_x86-64_unix.o \
	Lyra2.o Lyra2RE.o Sponge.o geek.o  \
	heavyhash/heavyhash.o heavyhash/keccak_tiny.o \
	verthash/tiny_sha3/sha3.o verthash/h2.o \
	equi/util.o equi/support/cleanse.o equi/random.o \
	equi/uint256.o equi/arith_uint256.o equi/crypto/hmac_sha512.o \
	equi/crypto/sha1.o equi/crypto/sha512.o equi/crypto/sha256.o \
	equi/crypto/hmac_sha256.o equi/crypto/equihash.o equi/crypto/ripemd160.o \
	equi/equihashverify.o sha512_256.o sha256dt.o \
        skydoge.o yescrypt/sha256.o yescrypt/yescrypt.o yescrypt/yescrypt-opt.o \
        yespower/crypto/blake2b-yp.o yespower/yespower-blake2b.o yespower/yespower-combined.o yespower/yespower-platform.o \
        minotaur/crypto/sha256.o minotaur/crypto/yespower.o minotaur/minotaurx.o \
        fishhash/3rdParty/fishhash_keccak.o fishhash/fishhash.o \
        flex/cryptonote/crypto/aesb.o flex/cryptonote/crypto/c_blake256.o flex/cryptonote/crypto/c_groestl.o flex/cryptonote/crypto/c_jh.o \
        flex/cryptonote/crypto/c_keccak.o flex/cryptonote/crypto/c_skein.o flex/cryptonote/crypto/hash.o flex/cryptonote/crypto/oaes_lib.o flex/cryptonote/crypto/wild_keccak.o \
        flex/cryptonote/cryptonight.o flex/cryptonote/cryptonight_dark.o flex/cryptonote/cryptonight_dark_lite.o flex/cryptonote/cryptonight_fast.o \
        flex/cryptonote/cryptonight_lite.o flex/cryptonote/cryptonight_soft_shell.o flex/cryptonote/cryptonight_turtle.o flex/cryptonote/cryptonight_turtle_lite.o \
        flex/flex.o \
        chacha20/chacha20.o xelishash/xelishashv1.o xelishash/xelishashv2.o 

all: $(TARGET)

$(TARGET): $(OBJECTS)
	$(CXX) $(LDFLAGS) -o $@ $^ $(LDLIBS)

.PHONY: clean

clean:
	$(RM) $(TARGET) $(OBJECTS)
