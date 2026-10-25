// nCTRL's Core (C#, NativeAOT) for the Share extension: see Native/iOS/CoreLib/Exports.cs.
#ifndef NCTRL_CORE_H
#define NCTRL_CORE_H
char *nctrl_core_call(const char *operation, const char *json);
void nctrl_core_free(char *text);
#endif
