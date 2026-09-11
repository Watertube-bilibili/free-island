# Native Windows 7 toolchain

The build uses the portable, pure 32-bit **w64devkit i686 1.23.0** release by Christopher Wellons, containing GCC 14.1.0, MinGW-w64 11.0.1, and binutils 2.42. Upstream marks this variant Windows XP compatible with a Pentium 4 minimum CPU; the application deliberately targets Windows 7 / subsystem 6.1.

- Official release: https://github.com/skeeto/w64devkit/releases/tag/v1.23.0
- Archive: https://github.com/skeeto/w64devkit/releases/download/v1.23.0/w64devkit-i686-1.23.0.zip
- Archive SHA-256: `033ef6a29bfcb5ef4452ec5219ed74b64b95d56ae71c9252996e77dd777ab7bf`
- Compiler: `tools/w64devkit-i686-1.23.0/w64devkit/bin/g++.exe`

The download above was obtained directly from the upstream GitHub release. The recorded SHA-256 is a local integrity value, not a separately authenticated upstream signature verification.

No global installation or PATH change is necessary. The build passes GCC's `-B` option to locate the matching assembler and linker. C++ and GCC runtimes are statically linked. The target uses Windows' built-in MSVCRT, not UCRT or the Visual C++ redistributable. The script checks the final application's and installer's imported DLLs and PE headers; Windows 7 machine testing remains separate from build-time compatibility checks.

Distribute `COPYING.MinGW-w64-runtime.txt` with released executables, as requested by the toolchain. The native packaging script includes it in both the ZIP and the installer.
