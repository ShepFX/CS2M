import {bindValue, trigger, useValue} from "cs2/api";
import {useLocalization} from "cs2/l10n";
import mod from "../../mod.json";
import styles from "./server-browser.module.scss";

// Sessions from {LobbyUrl}/sessions, fetched by UISystem.RefreshLobby
const sessionsBinding = bindValue<string>(mod.id, 'LobbySessions', '[]');
const statusBinding = bindValue<string>(mod.id, 'LobbyStatus', 'idle');

interface LobbySession {
    token: string;
    name: string | null;
    host: string | null;
    players: number;
    maxPlayers: number;
    hasPassword: boolean;
    gameVersion: string | null;
    modVersion: string | null;
}

function parseSessions(json: string): LobbySession[] {
    try {
        const list = JSON.parse(json);
        return Array.isArray(list) ? list.filter(s => s && typeof s.token === "string") : [];
    } catch {
        return [];
    }
}

export const ServerBrowser = ({enabled}: { enabled: boolean }) => {
    const {translate} = useLocalization();
    const sessions = parseSessions(useValue(sessionsBinding));
    const status = useValue(statusBinding);

    let message = null;
    if (status === "loading") {
        message = translate("CS2M.UI.Lobby.Loading");
    } else if (status === "error") {
        message = translate("CS2M.UI.Lobby.Error");
    } else if (sessions.length === 0) {
        message = translate("CS2M.UI.Lobby.Empty");
    }

    return (
        <div className={styles.browser}>
            <div className={styles.header}>
                <span className={styles.city}>{translate("CS2M.UI.Lobby.City")}</span>
                <span className={styles.host}>{translate("CS2M.UI.Lobby.Host")}</span>
                <span className={styles.players}>{translate("CS2M.UI.Lobby.Players")}</span>
                <button className={styles.button} disabled={status === "loading"}
                        onClick={() => trigger(mod.id, "RefreshLobby")}>
                    {translate("CS2M.UI.Lobby.Refresh")}
                </button>
            </div>
            <div className={styles.list}>
                {sessions.map(session => (
                    <div className={styles.row} key={session.token}
                         title={[session.gameVersion, session.modVersion && "CS2M " + session.modVersion].filter(v => v).join(" · ")}>
                        <span className={styles.city}>
                            {session.hasPassword &&
                                <img className={styles.lock} src="Media/Glyphs/Lock.svg"
                                     title={translate("CS2M.UI.Lobby.Password") ?? ""}/>}
                            {session.name || translate("CS2M.UI.Lobby.Unnamed")}
                        </span>
                        <span className={styles.host}>{session.host || "-"}</span>
                        <span className={styles.players}>
                            {session.maxPlayers > 0 ? `${session.players}/${session.maxPlayers}` : session.players}
                        </span>
                        <button className={styles.button} disabled={!enabled}
                                onClick={() => trigger(mod.id, "JoinSession", session.token)}>
                            {translate("CS2M.UI.Lobby.Join")}
                        </button>
                    </div>
                ))}
                {message && <div className={styles.message}>{message}</div>}
            </div>
        </div>
    );
}
