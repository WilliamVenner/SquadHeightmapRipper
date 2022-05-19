#pragma warning disable SYSLIB0011
#pragma warning disable IDE0090

using CommandLine;
using CommandLine.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;

namespace SquadHeightmapRipper
{
	struct FVec4
	{
		public float X, Y, Z, W;

		public FVec4(float X, float Y, float Z, float W)
		{
			this.X = X;
			this.Y = Y;
			this.Z = Z;
			this.W = W;
		}
	}

	struct Vec2 : IComparable<Vec2>
	{
		public int X, Y;

		public Vec2(int X, int Y)
		{
			this.X = X;
			this.Y = Y;
		}

		public int CompareTo(Vec2 other)
		{
			int result = X.CompareTo(other.X);
			if (result != 0) return result;
			return Y.CompareTo(other.Y);
		}
	}

	class LandscapeComponent
	{
		public int SectionBaseX;
		public int SectionBaseY;
		public int ComponentSizeQuads;
		public int SubsectionSizeQuads;
		public int NumSubsections;
		public FVec4 HeightmapScaleBias;
		public UTexture2D? HeightmapTexture;

		public void GetLandscapeExtent(ref int MinX, ref int MinY, ref int MaxX, ref int MaxY)
		{
			MinX = Math.Min(SectionBaseX, MinX);
			MinY = Math.Min(SectionBaseY, MinY);
			MaxX = Math.Max(SectionBaseX + ComponentSizeQuads, MaxX);
			MaxY = Math.Max(SectionBaseY + ComponentSizeQuads, MaxY);
		}
	}

	class Layer
	{
		private int MinX = int.MaxValue;
		private int MinY = int.MaxValue;
		private int MaxX = int.MinValue;
		private int MaxY = int.MinValue;

		readonly IPackage package;

		public Layer(IPackage package)
		{
			this.package = package;
		}

		public Heightmap? GetHeightmap()
		{
			var LandscapeComponents = ExtractLandscapeComponents(package.GetExports());

			foreach (var LandscapeComponent in LandscapeComponents)
			{
				LandscapeComponent.Value.GetLandscapeExtent(ref MinX, ref MinY, ref MaxX, ref MaxY);
			}

			return BuildHeightMap(LandscapeComponents);
		}

		public bool HasHeightmap()
		{
			int i = 0;
			UObject? export;
			while ((export = package.GetExport(i++)) != null)
			{
				if (export.ExportType == "LandscapeComponent")
				{
					ushort valid = 0;

					foreach (var Property in export.Properties)
					{
						switch (Property.Name.PlainText)
						{
							case "HeightmapTexture":
							case "SectionBaseX":
							case "SectionBaseY":
							case "ComponentSizeQuads":
							case "SubsectionSizeQuads":
							case "NumSubsections":
							case "HeightmapScaleBias":
								valid++;
								break;
						}
					}

					if (valid == 7) return true;
				}
			}

			return false;
		}

		private static SortedDictionary<Vec2, LandscapeComponent> ExtractLandscapeComponents(IEnumerable<UObject> exports)
		{
			SortedDictionary<Vec2, LandscapeComponent> LandscapeComponents = new SortedDictionary<Vec2, LandscapeComponent>();

			foreach (var SerializedLandscapeComponent in exports)
			{
				if (SerializedLandscapeComponent.ExportType != "LandscapeComponent") continue;

				LandscapeComponent Extracted = new LandscapeComponent();

				ushort valid = 0;

				foreach (var Property in SerializedLandscapeComponent.Properties)
				{
					switch (Property.Name.PlainText)
					{
						case "HeightmapTexture":
							FPackageIndex pkg_index = (FPackageIndex)Property.Tag!.GenericValue!;
							UTexture2D? Heightmap = pkg_index!.Load<UTexture2D>();
							Extracted.HeightmapTexture = Heightmap!;
							break;

						case "SectionBaseX":
							Extracted.SectionBaseX = (int)Property.Tag!.GenericValue!;
							break;

						case "SectionBaseY":
							Extracted.SectionBaseY = (int)Property.Tag!.GenericValue!;
							break;

						case "ComponentSizeQuads":
							Extracted.ComponentSizeQuads = (int)Property.Tag!.GenericValue!;
							break;

						case "SubsectionSizeQuads":
							Extracted.SubsectionSizeQuads = (int)Property.Tag!.GenericValue!;
							break;

						case "NumSubsections":
							Extracted.NumSubsections = (int)Property.Tag!.GenericValue!;
							break;

						case "HeightmapScaleBias":
							FVector4 scale_bias = (FVector4)((UScriptStruct)Property.Tag!.GenericValue!).StructType;
							Extracted.HeightmapScaleBias = new FVec4(scale_bias.X, scale_bias.Y, scale_bias.Z, scale_bias.W);
							break;

						default:
							continue;
					}

					valid++;
				}

				if (valid != 7) continue;

				Vec2 XY = new Vec2(
					Extracted.SectionBaseX / Extracted.ComponentSizeQuads,
					Extracted.SectionBaseY / Extracted.ComponentSizeQuads
				);

				LandscapeComponents.Add(XY, Extracted);
			}

			return LandscapeComponents;
		}

		private Heightmap? BuildHeightMap(SortedDictionary<Vec2, LandscapeComponent> LandscapeComponents)
		{
			int Stride = (1 + MaxX - MinX);
			Heightmap Heightmap = new Heightmap((uint)((MaxX - MinX) + 1), (uint)((MaxY - MinY) + 1));

			if (LandscapeComponents.Count == 0 || Heightmap.Width <= 2 || Heightmap.Height <= 2)
			{
				return null;
			}

			CalcComponentIndicesNoOverlap(MinX, MinY, MaxX, MaxY, LandscapeComponents.First().Value.ComponentSizeQuads, out int ComponentIndexX1, out int ComponentIndexY1, out int ComponentIndexX2, out int ComponentIndexY2);

			Parallel.For(ComponentIndexY1, ComponentIndexY2 + 1, ComponentIndexY =>
			{
				Parallel.For(ComponentIndexX1, ComponentIndexX2 + 1, ComponentIndexX =>
				{
					if (!LandscapeComponents.TryGetValue(new Vec2(ComponentIndexX, ComponentIndexY), out LandscapeComponent? Component) || Component == null) return;

					// Find coordinates of box that lies inside Component
					int ComponentX1 = Math.Clamp(MinX - ComponentIndexX * Component.ComponentSizeQuads, 0, Component.ComponentSizeQuads);
					int ComponentY1 = Math.Clamp(MinY - ComponentIndexY * Component.ComponentSizeQuads, 0, Component.ComponentSizeQuads);
					int ComponentX2 = Math.Clamp(MaxX - ComponentIndexX * Component.ComponentSizeQuads, 0, Component.ComponentSizeQuads);
					int ComponentY2 = Math.Clamp(MaxY - ComponentIndexY * Component.ComponentSizeQuads, 0, Component.ComponentSizeQuads);

					// Find subsection range for this box
					int SubIndexX1 = Math.Clamp((ComponentX1 - 1) / Component.SubsectionSizeQuads, 0, Component.NumSubsections - 1); // -1 because we need to pick up vertices shared between subsections
					int SubIndexY1 = Math.Clamp((ComponentY1 - 1) / Component.SubsectionSizeQuads, 0, Component.NumSubsections - 1);
					int SubIndexX2 = Math.Clamp(ComponentX2 / Component.SubsectionSizeQuads, 0, Component.NumSubsections - 1);
					int SubIndexY2 = Math.Clamp(ComponentY2 / Component.SubsectionSizeQuads, 0, Component.NumSubsections - 1);

					for (int SubIndexY = SubIndexY1; SubIndexY <= SubIndexY2; SubIndexY++)
					{
						for (int SubIndexX = SubIndexX1; SubIndexX <= SubIndexX2; SubIndexX++)
						{
						// Find coordinates of box that lies inside subsection
						int SubX1 = Math.Clamp(ComponentX1 - Component.SubsectionSizeQuads * SubIndexX, 0, Component.SubsectionSizeQuads);
							int SubY1 = Math.Clamp(ComponentY1 - Component.SubsectionSizeQuads * SubIndexY, 0, Component.SubsectionSizeQuads);
							int SubX2 = Math.Clamp(ComponentX2 - Component.SubsectionSizeQuads * SubIndexX, 0, Component.SubsectionSizeQuads);
							int SubY2 = Math.Clamp(ComponentY2 - Component.SubsectionSizeQuads * SubIndexY, 0, Component.SubsectionSizeQuads);

							// Update texture data for the box that lies inside subsection
							for (int SubY = SubY1; SubY <= SubY2; SubY++)
							{
								for (int SubX = SubX1; SubX <= SubX2; SubX++)
								{
									int LandscapeX = SubIndexX * Component.SubsectionSizeQuads + ComponentIndexX * Component.ComponentSizeQuads + SubX;
									int LandscapeY = SubIndexY * Component.SubsectionSizeQuads + ComponentIndexY * Component.ComponentSizeQuads + SubY;

									// Find the texture data corresponding to this vertex
									int SizeU = Component.HeightmapTexture!.SizeX;
									int SizeV = Component.HeightmapTexture!.SizeY;
									int HeightmapOffsetX = (int)((float)Component.HeightmapScaleBias.Z * (float)SizeU);
									int HeightmapOffsetY = (int)((float)Component.HeightmapScaleBias.W * (float)SizeV);

									int TexX = HeightmapOffsetX + (Component.SubsectionSizeQuads + 1) * SubIndexX + SubX;
									int TexY = HeightmapOffsetY + (Component.SubsectionSizeQuads + 1) * SubIndexY + SubY;

									// BGRA8
									var TexData = new ArraySegment<byte>(Component.HeightmapTexture.Mips[0].Data.Data, (TexX + TexY * SizeU) * 4, 4);

									ushort Height = (ushort)((((ushort)TexData[2]) << (ushort)8) | (ushort)TexData[1]);
									Heightmap.Data[(LandscapeY - MinY) * Stride + (LandscapeX - MinX)] = Height;
								}
							}
						}
					}
				});
			});

			return Heightmap;
		}

		private static void CalcComponentIndicesNoOverlap(int X1, int Y1, int X2, int Y2, int ComponentSizeQuads, out int ComponentIndexX1, out int ComponentIndexY1, out int ComponentIndexX2, out int ComponentIndexY2)
		{
			// Find Component range for this block of data
			ComponentIndexX1 = (X1 >= 0) ? X1 / ComponentSizeQuads : (X1 + 1) / ComponentSizeQuads - 1; // -1 because we need to pick up vertices shared between components
			ComponentIndexY1 = (Y1 >= 0) ? Y1 / ComponentSizeQuads : (Y1 + 1) / ComponentSizeQuads - 1;
			ComponentIndexX2 = (X2 - 1 >= 0) ? (X2 - 1) / ComponentSizeQuads : (X2) / ComponentSizeQuads - 1;
			ComponentIndexY2 = (Y2 - 1 >= 0) ? (Y2 - 1) / ComponentSizeQuads : (Y2) / ComponentSizeQuads - 1;
			// Shrink indices for shared values
			if (ComponentIndexX2 < ComponentIndexX1)
			{
				ComponentIndexX2 = ComponentIndexX1;
			}
			if (ComponentIndexY2 < ComponentIndexY1)
			{
				ComponentIndexY2 = ComponentIndexY1;
			}
		}
	}

	public struct Heightmap
	{
		public uint Width;
		public uint Height;
		public ushort[] Data;

		public Heightmap(uint Width, uint Height)
		{
			this.Width = Width;
			this.Height = Height;
			Data = new ushort[Width * Height];
		}
	}

	public class SquadHeightmapRipper : IDisposable
	{
		private readonly DefaultFileProvider provider;

		public SquadHeightmapRipper(string paks_path, string? aes_key)
		{
			provider = new DefaultFileProvider(paks_path, SearchOption.TopDirectoryOnly);
			provider.Initialize();

			if (aes_key != null)
			{
				provider.SubmitKey(new FGuid(0U), new FAesKey(aes_key));
			}
		}

		public Heightmap? ExportHeightMap(string umap_path)
		{
			return new Layer(provider.LoadPackage(umap_path)).GetHeightmap();
		}

		public IEnumerable<string> GetUMaps()
		{
			return provider.Files.Keys.Where(key => key.EndsWith(".umap"));
		}

		public void Dispose()
		{
			provider.Dispose();
			GC.SuppressFinalize(this);
		}
	}

	public class Program
	{
		class Options
		{
			[Option('k', "aes", Required = false, HelpText = "AES decryption key for packages")]
			public string? AES { get; set; }

			[Option('p', "paks", Required = true, HelpText = "Path of directory containing pak files")]
			public string? Paks { get; set; }

			[Option('m', "umap", Required = false, HelpText = "Path of umap to extract heightmap from; if not provided a list of found umaps will be output")]
			public string? Umap { get; set; }
		}

		static void Main(string[] args)
		{
			try
			{
				var result = new Parser(with => with.HelpWriter = null).ParseArguments<Options>(args);
				result.WithParsed(o =>
				{
					using SquadHeightmapRipper ripper = new SquadHeightmapRipper(o.Paks!, o.AES);

					if (o.Umap != null)
					{
						using Stream Stdout = Console.OpenStandardOutput();
						using BinaryWriter BinWrite = new BinaryWriter(Stdout);

						Heightmap? Heightmap = ripper.ExportHeightMap(o.Umap);
						if (Heightmap != null)
						{
							BinWrite.Write(Heightmap.Value.Width);
							BinWrite.Write(Heightmap.Value.Height);
							foreach (short height in Heightmap.Value.Data)
							{
								BinWrite.Write(height);
							}
						} else
						{
							BinWrite.Write(0);
							BinWrite.Write(0);
						}
					}
					else
					{
						foreach (var path in ripper.GetUMaps())
						{
							Console.WriteLine(path);
						}
					}
				})
				.WithNotParsed(errs =>
				{
					Console.WriteLine(HelpText.AutoBuild(result, h =>
					{
						h.AdditionalNewLineAfterOption = false;
						return HelpText.DefaultParsingErrorsHandler(result, h);
					}, e => e));
					Environment.Exit(1);
				});
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex.ToString());
				Environment.Exit(1);
			}
		}
	}
}