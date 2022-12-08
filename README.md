# Tashi Network Transport (TNT)

This package enables use of the Tashi Consensus Engine (TCE) as a Netcode
transport.

## Getting started

1. Install Unity Hub.
2. Install Unity Editor version 2021.3.15f1 through Unity Hub.
3. On Ubuntu 22.04 I had to install `libssl1.0.0_1.0.2n-1ubuntu5.10_amd64.deb`
4. `git clone -b use-tnt git@github.com:tashigg/com.unity.multiplayer.samples.coop.git`
    * Note that this is using the `use-tnt` branch. This will soon be merged to the main branch.
5. `Edit > Project Settings... > Services > Set up your organization`, which 
   should have Relay and Lobby enabled. This is covered in the Boss Room guide.
   There is a Tashi organization in Unity that you should be a member of. If
   you're using your own organization then you'll need to enable the Lobby service.
6. Set up symbolic links:
    ```bash
    tashi_platform_dir="$HOME/work/tashi/platform"
    game_dir="$HOME/work/tashi/com.unity.multiplayer.samples.coop"

    tnt_dir="$tashi_platform_dir/integrations/network.tashi.netcode.transport.tnt"
    ln -fs "$tashi_platform_dir/target/debug/libtashi_platform.*" "$tnt_dir/Runtime/"
    ln -fs "$tnt_dir" "$game_dir/Packages/"
    ```
    Absolute paths are required because when you build the game in Unity it will
    not resolve relative paths.
7. Enable TNT and configure its settings.
   * Select the NetworkManager in the Heirarchy tool.
   * Choose Tashi Network Transport as the Network Transport. This might
     require removing Unity Transport.
   * Scroll down in the inspector to the Tashi Network Transport  and set the
     expected number of nodes and the sync interval.
. `File > Build Settings` and click build.
