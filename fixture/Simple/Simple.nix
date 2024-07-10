{
    stdenv,
    buildDotnetModule,
    projects,
    dotnetCorePackages,
    ...
} : buildDotnetModule {
    pname = "Simple";
    version = "0.0.1";
    dotnet-sdk = dotnetCorePackages.sdk_8_0;
    dotnet-runtime = dotnetCorePackages.runtime_8_0;
    nugetDeps = ./deps.nix;
    postPatch=''
        cat << EOF > 'Simple.csproj.user'
        <Project><ItemGroup /></Project>
        EOF
    '';
    deps = [
    ];
    projectFile = "./Simple.csproj";
    src = [
        ./Class1.cs
        ./SomeNamespace/SomeClass.cs
        ../SomeFileToInclude.txt
        ./Simple.csproj
        ./deps.nix
    ];
    unpackPhase = ''
        for srcFile in $src; do
            cp -v "$srcFile" "$(stripHash "$srcFile")"
        done
    '';
}
