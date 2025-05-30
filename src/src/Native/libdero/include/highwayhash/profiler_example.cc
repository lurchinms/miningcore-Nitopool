// Copyright 2017 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#include <cassert>
#include <cmath>
#include <cstdlib>

#include "os_specific.h"
#include "profiler.h"

namespace highwayhash {
namespace {

void Spin(const double min_time) {
  const double t0 = Now();
  for (;;) {
    const double elapsed = Now() - t0;
    if (elapsed > min_time) {
      break;
    }
  }
}

void Spin10() {
  PROFILER_FUNC;
  Spin(10E-6);
}

void Spin20() {
  PROFILER_FUNC;
  Spin(20E-6);
}

void Spin3060() {
  {
    PROFILER_ZONE("spin30");
    Spin(30E-6);
  }
  {
    PROFILER_ZONE("spin60");
    Spin(60E-6);
  }
}

void Level3() {
  PROFILER_FUNC;
  for (int rep = 0; rep < 10; ++rep) {
    double total = 0.0;
    for (int i = 0; i < 100 - rep; ++i) {
      total += pow(0.9, i);
    }
    if (std::abs(total - 9.999) > 1E-2) {
      abort();
    }
  }
}

void Level2() {
  PROFILER_FUNC;
  Level3();
}

void Level1() {
  PROFILER_FUNC;
  Level2();
}

void ProfilerExample() {
  PinThreadToRandomCPU();
  {
    PROFILER_FUNC;
    Spin10();
    Spin20();
    Spin3060();
    Level1();
  }
  PROFILER_PRINT_RESULTS();
}

}  // namespace
}  // namespace highwayhash

int main(int argc, char* argv[]) {
  highwayhash::ProfilerExample();
  return 0;
}
