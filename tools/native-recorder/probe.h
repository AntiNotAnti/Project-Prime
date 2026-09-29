#pragma once
#include "NDS.h"
#include <string>
namespace melonDS {
void PrimeNativeProbe(u32 pc, const u32* registers);
void PrimeProbeBegin(NDS& nds, u32 player);
std::string PrimeProbeEnd();
}
