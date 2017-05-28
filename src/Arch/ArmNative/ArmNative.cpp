// ArmNative.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "types.h"
#include "reko.h"
#include "ArmRewriter.h"

extern "C" {
	
	DLLEXPORT INativeRewriter *
		CreateNativeRewriter(
			const uint8_t * rawBytes,
			uint32_t length,	
			uint32_t offset, 
			uint64_t address, 
			INativeRtlEmitter * m,
			INativeRewriterHost * host)
	{
		return new ArmRewriter(rawBytes + offset, length - offset, address, m, host);
	}
}