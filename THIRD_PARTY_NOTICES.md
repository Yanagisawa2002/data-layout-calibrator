# Third-party notices

This repository does not vendor Unity, Burst, Collections, Mathematics, Test Framework, or their binaries.

The UPM package declares dependencies on Unity packages. Those packages are obtained from the Unity installation or Unity Package Manager and remain governed by their respective licenses and terms. Generated local Player builds are excluded from this repository.

The Source Generator build uses the Microsoft.CodeAnalysis.CSharp NuGet package as a private build-time dependency; it is not bundled into the distributed analyzer DLL. Its standalone tests use NUnit and the Microsoft .NET test SDK. The optional fixed-result renderer requires Pillow at execution time. These dependencies are restored by their package managers and are not vendored in this repository.
