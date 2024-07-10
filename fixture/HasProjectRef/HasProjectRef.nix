{
    stdenv,
    buildDotnetModule,
    projects,
    dotnetCorePackages,
    ...
} : buildDotnetModule {
    pname = "HasProjectRef";
    version = "0.0.1";
    dotnet-sdk = dotnetCorePackages.sdk_8_0;
    dotnet-runtime = dotnetCorePackages.runtime_8_0;
    nugetDeps = ./deps.nix;
    postPatch=''
        cat << EOF > '.nix-build.props'
        <Project><ItemGroup><Reference Include="Simple"><HintPath>${projects."./Simple/Simple.csproj"}/lib/Simple/Simple.dll</HintPath></Reference><Remove Include="..\Simple\Simple.csproj" /></ItemGroup></Project>
        EOF
        cat .nix-build.props
    '';
    deps = [
    ];
    projectFile = "./HasProjectRef.csproj";
    src = [
        ./Class2.cs
        ./HasProjectRef.csproj
        ./deps.nix
    ];
    unpackPhase = ''
        for srcFile in $src; do
            cp -v "$srcFile" "$(stripHash "$srcFile")"
        done
    '';
}
