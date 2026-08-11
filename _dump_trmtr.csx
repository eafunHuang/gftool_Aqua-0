using System;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;
using GFTool.Core.Utils;

var trmtr = FlatBufferConverter.DeserializeFrom<TrmtrFile>(@"e:\za_mega\ZA-Poke\pm0121_51_00\pm0121_51_00.trmtr");
var mat = trmtr.Materials.FirstOrDefault(m => m.Name == "body_c");
if (mat == null) { Console.WriteLine("not found"); return; }
foreach (var f in mat.FloatParameters.OrderBy(x => x.Name))
    Console.WriteLine($"F {f.Name} = {f.Value}");
foreach (var v in mat.Float4Parameters.OrderBy(x => x.Name))
    Console.WriteLine($"V4 {v.Name} = {v.Value.X},{v.Value.Y},{v.Value.Z},{v.Value.W}");
foreach (var s in mat.Shaders.SelectMany(sh => sh.Values).Where(p => p.Name.Contains("Enable") || p.Name.Contains("Fresnel")))
    Console.WriteLine($"OPT {s.Name} = {s.Value}");
