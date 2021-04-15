// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full
// license information.

#include "class_factory.h"
#include "cor_profiler.h"
#include "logging.h"
#include "version.h"
#include <iostream>

ClassFactory::ClassFactory() : refCount(0) {}

ClassFactory::~ClassFactory() {}

HRESULT STDMETHODCALLTYPE ClassFactory::QueryInterface(REFIID riid,
                                                       void** ppvObject) {
  if (riid == IID_IUnknown || riid == IID_IClassFactory) {
    std::cout << "Interface found." << std::endl;
    *ppvObject = this;
    this->AddRef();
    return S_OK;
  }

  std::cout << "Interface not found." << std::endl;
  *ppvObject = nullptr;
  return E_NOINTERFACE;
}

ULONG STDMETHODCALLTYPE ClassFactory::AddRef() {
  return std::atomic_fetch_add(&this->refCount, 1) + 1;
}

ULONG STDMETHODCALLTYPE ClassFactory::Release() {
  int count = std::atomic_fetch_sub(&this->refCount, 1) - 1;
  if (count <= 0) {
    delete this;
  }

  return count;
}

// profiler entry point
HRESULT STDMETHODCALLTYPE ClassFactory::CreateInstance(IUnknown* pUnkOuter,
                                                       REFIID riid,
                                                       void** ppvObject) {
  std::cout << "Creating profiler instance." << std::endl;

  if (pUnkOuter != nullptr) {
    std::cout << "Class doesn't support aggregations." << std::endl;
    *ppvObject = nullptr;
    return CLASS_E_NOAGGREGATION;
  }

  trace::Info("Datadog CLR Profiler ", PROFILER_VERSION,
              " on",

#ifdef _WIN32
              " Windows"
#elif MACOS
              " macOS"
#else
              " Linux"
#endif

#ifdef AMD64
            , " (amd64)"
#elif X86
            , " (x86)"
#elif ARM64
            , " (arm64)"
#elif ARM
            , " (arm)"
#endif
  );
  trace::Debug("ClassFactory::CreateInstance");

  std::cout << "Initializing profiler." << std::endl;
  auto profiler = new trace::CorProfiler();
  std::cout << "Querying interface." << std::endl;
  return profiler->QueryInterface(riid, ppvObject);
}

HRESULT STDMETHODCALLTYPE ClassFactory::LockServer(BOOL fLock) { return S_OK; }
