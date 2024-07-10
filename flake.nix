{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    flake-parts.url = "github:hercules-ci/flake-parts";
  };

  outputs = inputs @ {self, ...}:
    inputs.flake-parts.lib.mkFlake {inherit inputs;} {
      systems = [
        "aarch64-darwin"
        "x86_64-darwin"
        "x86_64-linux"
      ];

      perSystem = {pkgs, ...}: let
        fixture = import ./fixture/fixture.nix {
          inherit (pkgs) stdenv buildDotnetModule dotnetCorePackages;
        };
      in {
        formatter = pkgs.alejandra;
        packages = {
          inherit (fixture) simple hasProjectRef;
        };
        devShells = {
          default = pkgs.mkShell {
            nativeBuildInputs = with pkgs; [
              dotnetCorePackages.sdk_8_0
            ];

            DOTNET_BIN = "${pkgs.dotnetCorePackages.sdk_8_0}/bin/dotnet";
          };
        };
      };
    };
}
