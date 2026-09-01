# Compatibility matrix

| Component | Declared minimum | Verified |
|---|---:|---:|
| Unity | 6000.0 | 6000.5.3f1 |
| Burst | 1.8.29 | 1.8.29 |
| Collections | 2.6.3 | 6.5.0 built-in |
| Mathematics | 1.3.2 | 1.4.0 built-in |
| Platform | Windows x64 | Windows 11 x64 |
| Scripting backend | Mono or IL2CPP | Both verified for both Samples in non-Development Release Players |
| Burst AOT | Required | 1.8.29 library and required entrypoint manifest verified for Mono and IL2CPP |
| Source Generator target | .NET Standard 2.0 | Unity 6000.5.3f1 Roslyn host and standalone Roslyn 4.0.1 tests |
| Fixed-result renderer | Python 3.10+ and Pillow | Python 3.12.10 and Pillow 12.3.0 |

Only the verified column is evidence. Other compatible versions remain hypotheses until their Player tests pass.
