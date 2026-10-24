# Applying the CS2 lobby hardening on the main VPS

Three independent parts, applied in this order. Each can be checked and rolled back on its own.

1. The new lobby image and a hardened `docker-compose.yml` for `/opt/docker/CS2Lobby`.
2. nginx: `cs2.shep.rip` only reachable through Cloudflare, rate limits, an anonymised log, and a catch-all
   default server so the bare IP no longer answers as the lobby.
3. nftables: per-source and global rate limits on udp/4242, before Docker.

Files (in this directory):

| File | Goes to |
|---|---|
| `docker-compose.yml` | `/opt/docker/CS2Lobby/docker-compose.yml` |
| `nginx/00-cs2-lobby.conf` | `/opt/docker/Utilities/nginx/conf.d/00-cs2-lobby.conf` (new) |
| `nginx/000-default.conf` | `/opt/docker/Utilities/nginx/conf.d/000-default.conf` (new) |
| `nginx/cs2.shep.rip.conf` | `/opt/docker/Utilities/nginx/conf.d/cs2.shep.rip.conf` (replaces) |
| `cs2-lobby-firewall` | `/usr/local/sbin/cs2-lobby-firewall` (new, mode 755) |
| `cs2-lobby-firewall.service` | `/etc/systemd/system/cs2-lobby-firewall.service` (new) |

All commands run as root. "On Shep" means in `/opt/build/cs2m-wt/fix-lobby` on Shep; everything else is on the VPS.

## 0. Before you start

On Shep, note what the sites answer now, to compare afterwards:

```sh
for h in cs2.shep.rip shep.rip stats.shep.rip servers.shep.rip sails.shep.rip meteor.shep.rip knights.shep.rip \
         fastdl.shep.rip cukservers.net stats.cukservers.net fastdl.cukservers.net; do
  curl -s -o /dev/null -w "$h %{http_code}\n" https://$h/
done > /tmp/sites-before.txt; cat /tmp/sites-before.txt
```

Backups on the VPS (one folder, delete it once everything has run for a week):

```sh
B=/root/backup/cs2-lobby-$(date +%Y%m%d); mkdir -p $B
cp -p /opt/docker/CS2Lobby/docker-compose.yml $B/
tar -C /opt/docker/CS2Lobby -czf $B/src.tgz src
cp -p /opt/docker/Utilities/nginx/conf.d/cs2.shep.rip.conf $B/
docker tag cs2m-lobby:local cs2m-lobby:pre-hardening
nft list ruleset > $B/nft-ruleset-before.txt
```

Copy the files over, from Shep:

```sh
cd /opt/build/cs2m-wt/fix-lobby
tar -cf - CS2M.Lobby | ssh root@37.187.250.23 'rm -rf /opt/docker/CS2Lobby/src.new && mkdir -p /opt/docker/CS2Lobby/src.new && tar -C /opt/docker/CS2Lobby/src.new -xf -'
tar -C deploy -cf - cs2-lobby | ssh root@37.187.250.23 'rm -rf /root/cs2-lobby-deploy && mkdir -p /root/cs2-lobby-deploy && tar -C /root/cs2-lobby-deploy --strip-components=1 -xf -'
```

## 1. Lobby image and compose file

The new lobby accepts today's mod unchanged (no secret needed). Recreating the container empties the session
list; hosts re-register by themselves within 10 s, but a join that is mid-introduction at that moment fails and
has to be retried. Prefer a moment with no sessions:

```sh
docker exec cs2m-lobby dotnet CS2M.Lobby.dll --healthcheck; echo rc=$?   # 0 = healthy
curl -s https://cs2.shep.rip/health; echo                                 # "sessions": N
```

Then:

```sh
cd /opt/docker/CS2Lobby
mv src src.old && mv src.new src
docker build -t cs2m-lobby:local src/CS2M.Lobby
cp /root/cs2-lobby-deploy/docker-compose.yml docker-compose.yml
docker compose config -q && docker compose up -d
rm -rf src.old    # the copy is in the backup folder
```

Check:

```sh
docker ps --filter name=cs2m-lobby --format '{{.Status}}'          # "Up ... (healthy)" after about 30 s
docker inspect cs2m-lobby --format 'ro={{.HostConfig.ReadonlyRootfs}} caps={{.HostConfig.CapDrop}} nnp={{.HostConfig.SecurityOpt}} pids={{.HostConfig.PidsLimit}} cpus={{.HostConfig.NanoCpus}} mem={{.HostConfig.Memory}} swap={{.HostConfig.MemorySwap}} autoheal={{index .Config.Labels "autoheal"}}'
ss -lunp | grep 4242                                                  # only 0.0.0.0:4242, no [::]:4242
docker logs --since 5m cs2m-lobby                                     # no errors; "UDP last minute: ..." once a minute
```

UDP end to end, from Shep (registers a test session with a secret, lists it over https, introduces and connects a
joiner, closes it; it uses one of Shep's 5 distinct introductions per hour):

```sh
T=$(mktemp -d); cp /opt/build/cs2m-wt/fix-lobby/CS2M.Lobby.SmokeTest/* $T/
docker run --rm --network host -v $T:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run -- relay.shep.rip 4242 https://cs2.shep.rip --remote
rm -rf $T
```

Rollback: `cp $B/docker-compose.yml /opt/docker/CS2Lobby/ && docker tag cs2m-lobby:pre-hardening cs2m-lobby:local && cd /opt/docker/CS2Lobby && docker compose up -d`
(and `rm -rf src && tar -C /opt/docker/CS2Lobby -xzf $B/src.tgz` for the old source).

## 2. nginx

```sh
D=/opt/docker/Utilities/nginx/conf.d
cp /root/cs2-lobby-deploy/nginx/00-cs2-lobby.conf /root/cs2-lobby-deploy/nginx/000-default.conf $D/
cp /root/cs2-lobby-deploy/nginx/cs2.shep.rip.conf $D/cs2.shep.rip.conf
docker exec nginx nginx -t && docker exec nginx nginx -s reload
```

If `nginx -t` fails, nothing has changed yet: put the old file back and remove the two new ones (rollback below).

Check, from Shep:

```sh
curl -s https://cs2.shep.rip/health; echo                         # 200 JSON through Cloudflare
curl -s -D - -o /dev/null https://cs2.shep.rip/sessions | grep -i -c cache-control   # 1 (was 2)
curl -sk https://37.187.250.23/health; echo "rc=$?"               # rc=35: TLS handshake refused
curl -s  http://37.187.250.23/; echo "rc=$?"                       # rc=52: empty reply (444)
curl -sk --resolve cs2.shep.rip:443:37.187.250.23 https://cs2.shep.rip/health; echo "rc=$?"   # rc=92 or 52: 444
for i in $(seq 25); do curl -s -o /dev/null -w '%{http_code} ' https://cs2.shep.rip/health; done; echo   # 429s after ~20
for h in cs2.shep.rip shep.rip stats.shep.rip servers.shep.rip sails.shep.rip meteor.shep.rip knights.shep.rip \
         fastdl.shep.rip cukservers.net stats.cukservers.net fastdl.cukservers.net; do
  curl -s -o /dev/null -w "$h %{http_code}\n" https://$h/
done | diff /tmp/sites-before.txt - && echo "all sites as before"
```

On the VPS: `tail /opt/docker/Utilities/nginx/logs/cs2.access.log` shows addresses ending in `.0`, and
`grep -c 'GET /sessions' /opt/docker/Utilities/nginx/logs/access.log` stops growing. The old full-address lines
in `access.log*` age out with the normal 7-day rotation (`/etc/logrotate.d/cukservers` already covers
`cs2.access.log` through its `*.log` pattern).

Rollback:

```sh
rm $D/00-cs2-lobby.conf $D/000-default.conf
cp $B/cs2.shep.rip.conf $D/cs2.shep.rip.conf
docker exec nginx nginx -t && docker exec nginx nginx -s reload
```

## 3. UDP rate limits (nftables)

The VPS uses iptables-nft (Docker's rules), fail2ban's own `inet f2b-table`, and netfilter-persistent restoring
`/etc/iptables/rules.v4` (a September 2025 snapshot). The lobby limits live in their own table, `ip cs2lobby`,
hooked at prerouting raw priority (before conntrack and Docker's DNAT), and are applied at boot by their own unit.
Do NOT run `netfilter-persistent save` for this (it would also freeze Docker's current chains into rules.v4), and
do NOT enable `nftables.service` (`/etc/nftables.conf` starts with `flush ruleset`, which would wipe Docker's and
fail2ban's rules).

```sh
install -m 755 /root/cs2-lobby-deploy/cs2-lobby-firewall /usr/local/sbin/cs2-lobby-firewall
install -m 644 /root/cs2-lobby-deploy/cs2-lobby-firewall.service /etc/systemd/system/cs2-lobby-firewall.service
cs2-lobby-firewall --dry-run                 # prints the ruleset and "nft -c: ruleset OK"; applies nothing
systemctl daemon-reload
systemctl enable --now cs2-lobby-firewall
cs2-lobby-firewall --status                  # the table, with counters
```

Check: the last `counter` in chain `lobby` (accepted packets) keeps rising while the two drop counters stay at or
near 0 in normal use; `docker logs --since 2m cs2m-lobby | grep 'UDP last minute'` still shows refreshes; run the
`--remote` smoke test from step 1 again (from a fresh hour, or it will report the introduction as refused).
After the next reboot, `systemctl status cs2-lobby-firewall` should be "active (exited)" and `--status` show the table.

Rollback: `systemctl disable --now cs2-lobby-firewall` (its stop removes the table), or just
`cs2-lobby-firewall --remove` for now.

## Afterwards

- Delete the backup folder and `docker rmi cs2m-lobby:pre-hardening` after a week without problems.
- The mod can start sending a host secret (`TokenSecret`, see "Host secret" in the README) whenever it is ready;
  this lobby already accepts it, and older lobbies ignore it.

## Not part of this change, for the owner to decide

- `$shep_from_cf` (00-shep-cloudflare.conf, used by shep.rip.conf) trusts the Docker ranges 172.16.0.0/12,
  100.64.0.0/10 and 10.0.0.0/8. nginx is published on `[::]:80/443` too, and docker-proxy hands every IPv6
  connection to nginx from a Docker gateway address (100.64.10.1), so a direct IPv6 visitor to the origin passes
  shep.rip's Cloudflare-only check. `$cs2_from_cf` doesn't trust those ranges. For the other sites, either publish
  nginx on `0.0.0.0` only (no IPv6 on the origin; Cloudflare reaches it over IPv4 today: the access log has no
  Cloudflare requests from the gateway address) or drop the Docker ranges from that geo if nothing internal needs
  them.
- If an AAAA record for the origin is ever added behind Cloudflare, Cloudflare would reach nginx over IPv6 through
  docker-proxy and `$cs2_from_cf` would refuse it: enable IPv6 in Docker (so the real address arrives) first.
