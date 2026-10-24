# CS2M - Cities: Skylines 2 Multiplayer

[![Discord](https://img.shields.io/discord/508902220943851522.svg)](https://discord.gg/RjACPhd)

## Quick Links

- [Discord Server](https://discord.gg/RjACPhd)
- [Developer Resources](https://cs2.paradoxwikis.com/Modding)
- [Patreon](https://www.patreon.com/CSM_MultiplayerMod)

## Introduction

CS2M is an in-development multiplayer / co-op mod for Cities: Skylines 2. This mod aims to provide a simple client-server experience where users can play and build together in a single game.

Feel free to join the development Discord server [here](https://discord.gg/RjACPhd).

## Developer Resources

### Mod Dependencies

Other Cities Skylines 2 mods that are required for CS2M to function properly:

- [I18n EveryWhere](https://mods.paradoxplaza.com/mods/75426/Windows)

## Lobby server

`CS2M.Lobby/` is the API server the mod talks to: the server browser list, joining by token through NAT
hole punching, and the port check. It is a separate .NET 8 app, not part of the mod build.

The mod's settings point at it: `ApiServer` (default `relay.shep.rip`) and `ApiServerPort` (default `4242`)
for UDP, and the hidden `LobbyUrl` (default `https://cs2.shep.rip`) for the HTTP list.

### Ports

- **UDP 4242**: the mod's API messages (LiteNetLib unconnected messages carrying MessagePack) and NAT
  introductions. Must be reachable directly, not through an HTTP proxy.
- **TCP 8080**: plain HTTP JSON API, meant to sit behind a TLS reverse proxy.
  - `GET /sessions`: live sessions, `[{token, name, host, players, maxPlayers, hasPassword, gameVersion, modVersion, lastSeen}]`.
    Never contains IP addresses.
  - `GET /health`: `{status, sessions, uptimeSeconds}`.

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `LOBBY_UDP_PORT` | `4242` | UDP port for the mod |
| `LOBBY_HTTP_PORT` | `8080` | HTTP port for the JSON API |
| `LOBBY_HTTP_BIND` | `+` (all) | HTTP listen address |
| `LOBBY_SESSION_TTL` | `60` | Seconds without a keep-alive before a session is dropped (hosts send one every 10 s) |
| `LOBBY_MAX_SESSIONS` | `1000` | Session limit |
| `LOBBY_MAX_SESSIONS_PER_IP` | `5` | Sessions one public address may register |
| `LOBBY_LOG_IPS` | off | `1` logs full addresses; otherwise logs show only the first two octets |

### Build and run

```sh
cd CS2M.Lobby
docker build -t cs2m-lobby .
docker run -d --name cs2m-lobby --restart unless-stopped \
  -p 4242:4242/udp -p 127.0.0.1:8080:8080 cs2m-lobby
```

`CS2M.Lobby.SmokeTest/` plays a host and a joiner against a running server, serializing exactly like the
mod: `dotnet run --project CS2M.Lobby.SmokeTest -- <lobby host> 4242 8080`.

### Protocol

A message is a LiteNetLib unconnected message whose payload is what MessagePack.Attributeless writes:
`int typeKey, int propertyCount, (int propertyKey, value)...`. Type keys come from the registration order in
`ApiCommand.ProtocolOrder` (PortCheckRequest 0, PortCheckResult 1, ServerRegistration 2, SessionInfo 3,
ServerUnregister 4); property keys are the command's properties sorted by name. `CS2M.Lobby/Protocol.cs`
lists them. New commands go at the end of `ProtocolOrder`, and existing commands must not gain properties
(that renumbers them); add a new command instead.

## Contributors
A full list of contributors can be found [here](https://github.com/CitiesSkylinesMultiplayer/CS2M/graphs/contributors).

## License

This mod and its source code is licensed under the MIT license.