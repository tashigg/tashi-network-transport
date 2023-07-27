# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.2]

### Changed

* Significant performance improvements in terms of CPU, memory and lower
  latency.
* The default Tashi Relay URL now points to our East US service.
* Tashi Relay's API key is now masked in the inspector by default.
* The default editor version for the Examples project is now 2022.3.5f1.
* The Examples project asset now includes TNT.

## 0.3.1

This version was an internal release for our cloud services.

## [0.3.0]

### Added

* Tashi Relay will be used if an API key is set. This  is an alternative to Unity
  Relay which is optimized for our architecture. You can request an API key by
  joining our [Discord] server. Set your API key using the Unity Editor's
  Inspector for the TNT component.
* NetworkManager's log level is now passed through to our native library to
  allow you to tailor which log messages you see.

### Changed

* The LocalWithLobby example can now also work with Tashi Relay. In order to
  achieve this TNT now provides a way to modify lobby and lobby player data.
* The maximum session size when using Unity Relay has been set to 8.
  If you would like to support larger, higher performance sessions you should
  consider using Tashi Relay which is tailored to our architecture.

## [0.2.0]

### Added

* A sample script showing how you can use TNT over LAN with Unity Relay is
  included in the distributed package.
* An example project which uses the sample script is included in the TNT git
  repo.
* `TashiNetworkTransport` now has a `SessionHasStarted` property.
* Unity Relay is now supported, which enables communication over WAN. This is
  intended to be a temporary measure until Tashi Relay is released.

### Changed

* `SecretKey.GetPublicKey()` has become a property named `PublicKey`.

## [0.1.0]

This is the first public release and it's under heavy development, so please
be aware that things will change frequently.

Some features are going to be removed as soon as we're able to, such as the
Unity Relay integration. This is because it's unsuitable for our architecture,
but it enables people to start using TNT immediately.

### Current limitations and known issues

* Unity’s Netcode for Game Objects expects there to be a single host. When the
  host disconnects from a session a new host isn’t chosen in their place.
* The CPU load for processing events is high.
* The number of participants must be specified upfront.
* Players can't join an existing session.

[Keep a Changelog]: https://keepachangelog.com/en/1.0.0/
[Semantic Versioning]: https://semver.org/spec/v2.0.0.html
[Discord]: https://discord.com/invite/fPNdgUCGnk
[0.3.2]: https://github.com/tashigg/tashi-network-transport/releases/tag/v0.3.2
[0.3.0]: https://github.com/tashigg/tashi-network-transport/releases/tag/v0.3.0
[0.2.0]: https://github.com/tashigg/tashi-network-transport/releases/tag/v0.2.0
[0.1.0]: https://github.com/tashigg/tashi-network-transport/releases/tag/v0.1.0
