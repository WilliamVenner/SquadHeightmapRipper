Basic C# library/console application for ripping heightmaps from UE4 maps.

This was made for [Squad Mortar Helper](https://github.com/WilliamVenner/squad-mortar-helper) and is missing extendability features that would be appropriate for other games and environments. Currently the program only supports zero to one AES decryption key and is not optimised for bulk processing. **Please feel free to fork and make your own changes to the code if needed, PRs are also appreciated.**

_I don't know if this works for UE5._

# Usage

**Before building, don't forget to `git submodule update --recursive --remote --init`!**

```
UE4HeightmapRipper 1.0.0
Copyright (C) 2022 UE4HeightmapRipper

  -k, --aes     AES decryption key for packages
  -p, --paks    Required. Path of directory containing pak files
  -m, --umap    Required. Path of umap to extract heightmap from
  --help        Display this help screen.
  --version     Display version information.
```

## Output

In the event of an error, the program will exit with code 1 and print the error message to stderr.

In the event of success, the program will exit with code 0 and output the following to stdout:

1. Little endian 4-byte unsigned integer of width
2. Little endian 4-byte unsigned integer of height
3. Little endian 2-byte unsigned short of heightmap data

# License

I can't provide a license for this software because [CUE4Parse](https://github.com/FabianFG/CUE4Parse) is also unlicensed (see [issue #39](https://github.com/FabianFG/CUE4Parse/issues/39).)

Do whatever you want with my code, however.
