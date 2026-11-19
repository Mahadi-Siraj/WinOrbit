# Compiles an HLSL pixel shader for WPF (ShaderEffect) into its .ps bytecode.
# Uses D3DCompiler_47.dll, which ships with Windows 10 and 11 — no SDK or package needed.
#   .\tools\compile-shader.ps1 src\WinOrbit\Design\Shaders\LiquidGlass.fx
# writes LiquidGlass.ps next to it. Commit both: the app embeds the .ps; the .fx is the source.
param([Parameter(Mandatory)][string]$Source, [string]$Profile = "ps_2_0", [string]$Entry = "main")
Add-Type @'
using System; using System.Runtime.InteropServices;
[ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ID3DBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] IntPtr GetBufferSize(); }
public static class Hlsl {
  [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
  static extern int D3DCompile(byte[] src, IntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target, uint f1, uint f2, out ID3DBlob code, out ID3DBlob errors);
  static byte[] Bytes(ID3DBlob b) { var n = (int)b.GetBufferSize(); var a = new byte[n]; Marshal.Copy(b.GetBufferPointer(), a, 0, n); return a; }
  public static byte[] Compile(byte[] src, string name, string entry, string target, out string log) {
    ID3DBlob code, errors;
    // D3DCOMPILE_OPTIMIZATION_LEVEL3 (1 << 15)
    var hr = D3DCompile(src, (IntPtr)src.Length, name, IntPtr.Zero, IntPtr.Zero, entry, target, 1u << 15, 0, out code, out errors);
    log = errors == null ? "" : System.Text.Encoding.ASCII.GetString(Bytes(errors)).TrimEnd('\0');
    return hr < 0 || code == null ? null : Bytes(code);
  }
}
'@
$full = (Resolve-Path $Source).Path
$log = ""
$bytes = [Hlsl]::Compile([IO.File]::ReadAllBytes($full), [IO.Path]::GetFileName($full), $Entry, $Profile, [ref]$log)
if ($log) { Write-Output $log }
if ($null -eq $bytes) { throw "Shader did not compile." }
$out = [IO.Path]::ChangeExtension($full, ".ps")
[IO.File]::WriteAllBytes($out, $bytes)
Write-Output "Wrote $out ($($bytes.Length) bytes, $Profile)"
