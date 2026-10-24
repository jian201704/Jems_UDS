# Verify DoipFunctionLibrary.dll is discoverable by the JEMSts LibraryLoader rule.
# Run in a FRESH process so it does not lock the DLL for the build.
$root = "E:\SourceCode\JEMSts_UDS_Parser"

# Pre-load dependencies in dependency order (ScriptVariableSpace first, then JabilTest base)
# so that GetTypes() has everything resolvable (no recursive AssemblyResolve handler needed).
[Reflection.Assembly]::LoadFrom("$root\lib\ScriptVariableSpaceInterface.dll") | Out-Null
$baseAssembly = [Reflection.Assembly]::LoadFrom("$root\lib\Jabil_Test_Base_Function.dll")

$asm = [Reflection.Assembly]::LoadFrom("$root\artifacts\DoipFunctionLibrary.dll")
$baseType = $baseAssembly.GetType("JabilTest.Test")
Write-Output "== Test subclasses discovered by LibraryLoader rule =="
$found = 0
foreach ($type in $asm.GetTypes()) {
    if ($type.IsSubclassOf($baseType)) {
        $ctorCount = @($type.GetConstructors() | Where-Object { $_.GetParameters().Length -eq 2 }).Count
        Write-Output ("  {0}  abstract={1}  ctor(ITestCellVariableSpace,object)={2}" -f $type.Name, $type.IsAbstract, $ctorCount)
        $found++
    }
}
Write-Output "== total Test subclasses: $found =="
