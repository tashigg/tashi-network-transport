# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - Unreleased

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

[Keep a Changelog]: https://keepachangelog.com/en/1.0.0/
[Semantic Versioning]: https://semver.org/spec/v2.0.0.html
[0.0.1]: https://github.com/tashigg/tashi-network-transport/releases/tag/v0.0.1
