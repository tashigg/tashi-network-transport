# Tashi Network Transport (TNT)

TNT is a Unity Netcode for GameObjects transport that uses the Tashi Consensus
Engine (TCE) to provide distributed communication with fair ordering of events.

## Adding TNT to your project

1. Unzip the TNT package.
2. Open the Package Manager in Unity.
3. Select `+`, and choose to add package from disk.
4. Navigate to the unzipped TNT package and select `package.json`
5. Click Open.

Now you must configure `NetworkManager` to use TNT:
1. Open the NetworkManager in the Inspector.
2. Click the Network Transport.
3. Select Tashi Network Transport from the drop down.

## Pre-release Limitations

The number of nodes within the network must be decided upfront. This is
configurable within the Unity editor.

The network configuration must allow connections between nodes.

Each node's secret key is generated on start up.
