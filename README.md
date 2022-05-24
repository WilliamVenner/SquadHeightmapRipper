Basic C# library/console application for ripping heightmaps from Squad.

This was made for [Squad Mortar Helper](https://github.com/WilliamVenner/squad-mortar-helper)!

This could also be easily adapted for ripping heightmaps from other UE4 games.

# Usage

**Before building, don't forget to `git submodule update --recursive --remote --init`!**

```
SquadHeightmapRipper 2.0.1
Copyright (C) 2022 William Venner

  -k, --aes     AES decryption key for packages
  -p, --paks    Required. Path of directory containing pak files
  -m, --umap    Required. Path of umap to extract heightmap from
  --help        Display this help screen.
  --version     Display version information.
```

## Output

In the event of an error, the program will exit with code 1 and print the error message to stderr.

In the event of success, the program will exit with code 0 and output a bunch of [relevant data](SquadHeightmapRipper/Program.cs#L448) to stdout.

# License

I can't provide a license for this software because [CUE4Parse](https://github.com/FabianFG/CUE4Parse) is also unlicensed (see [issue #39](https://github.com/FabianFG/CUE4Parse/issues/39).)

Do whatever you want with my code, however.
