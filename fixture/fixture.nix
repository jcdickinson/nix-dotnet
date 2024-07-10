{
  stdenv,
  buildDotnetModule,
  ...
} @ args: let
  projects = rec {
    "./Simple/Simple.csproj" = import ./Simple/Simple.nix (args // {inherit projects;});
    "./HasProjectRef/HasProjectRef.csproj" = import ./HasProjectRef/HasProjectRef.nix (args // {inherit projects;});
  };
in {
  simple = projects."./Simple/Simple.csproj";
  hasProjectRef = projects."./HasProjectRef/HasProjectRef.csproj";
}
