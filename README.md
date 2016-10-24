# btcnfgen
Builds or extracts valid pspbtcnf files (for 5.50).

This is a project I created back in 2011 when I needed a custom pspbtcnf file for a custom firmware project based on 5.50 GEN. It is only tested on that firmware, and that's also the only firmware it is likely to work on, but it might provide you with a decent starting point to reverse engineer other versions.

**DISCLAIMER:** As always, when dealing with PSP flash memory, caution is advised and having a Pandora battery and magic memory stick on hand wouldn't hurt either. By using this tool you claim full responsibility for whatever damage it might cause to any hard or software.

```
Usage: btcnfgen [-b | -e | -h] [-v] [-y] source [output]
-b Build a binary file from a plaintext file.
-e Extract a binary file into a plaintext file.
-v Force version (in format #.##).
-c Ask for confirmation to overwrite if output file already exists.
-h Displays this info.
```
