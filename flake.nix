{
  description = "Subnautica Creator Royale: Nitrox tournament mod and caster website";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs, ... }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" "x86_64-darwin" "aarch64-darwin" ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});

      pythonFor = pkgs: pkgs.python3.withPackages (ps: [ ps.pyyaml ]);
    in
    {
      # `nix run .#check` runs ./scripts/check. Not a flake `checks` output: dotnet restore and
      # go mod download need network, which the nix build sandbox lacks.
      apps = forAllSystems (pkgs: {
        check = {
          type = "app";
          program = "${pkgs.writeShellScript "lms-check" ''
            export PATH="${pkgs.lib.makeBinPath [ pkgs.dotnetCorePackages.sdk_10_0 pkgs.go pkgs.nodejs (pythonFor pkgs) ]}:$PATH"
            exec ./scripts/check
          ''}";
        };
      });

      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          # Everything the documented commands need.
          packages = with pkgs; [
            dotnetCorePackages.sdk_10_0
            go
            gopls
            gotools
            # Only for the caster front-end's tests (`node --test`); the page has no build step.
            nodejs
            # For ./scripts/check's Python checks and the scripts' unittest suite.
            (pythonFor pkgs)
            git
          ];

          # The SDK is read-only in the store; without this, restore fails trying to
          # write its first-run marker.
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";
          DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";

          shellHook = ''
            echo "creator-royale -- dotnet $(dotnet --version 2>/dev/null || echo '?'), $(go version 2>/dev/null || echo 'go ?'), node $(node --version 2>/dev/null || echo '?')"
            echo "tests: ./scripts/check    (or: nix run .#check)"
            echo "src/Lms.Game and src/Lms.Server need a Subnautica install and ./scripts/nitrox-fork."
          '';
        };
      });
    };
}
