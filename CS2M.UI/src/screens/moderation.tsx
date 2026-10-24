import {bindValue, trigger, useValue} from "cs2/api";
import {useState} from "react";
import mod from "../../mod.json";
import styles from "./hud.module.scss";

const isHostBinding = bindValue<boolean>(mod.id, 'IsHost', false);
const noticeBinding = bindValue<string>(mod.id, 'DisconnectNotice', '');

// Host only: kick or ban one player from their row in the HUD. Ban asks to confirm first.
export const PlayerActions = ({id, name}: {id: number, name: string}) => {
    const isHost = useValue(isHostBinding);
    const [open, setOpen] = useState(false);
    const [confirmBan, setConfirmBan] = useState(false);
    if (!isHost) {
        return null;
    }

    const close = () => { setOpen(false); setConfirmBan(false); };
    const kick = (ban: boolean) => { trigger(mod.id, "KickPlayer", id, ban); close(); };

    if (!open) {
        return <button className={styles.more} onClick={() => setOpen(true)} title={`Moderate ${name}`}>⋯</button>;
    }

    return (
        <div className={styles.actions}>
            {confirmBan ? (
                <>
                    <span className={styles.confirm}>Ban {name}?</span>
                    <button className={styles.danger} onClick={() => kick(true)}>Ban</button>
                    <button className={styles.action} onClick={close}>Cancel</button>
                </>
            ) : (
                <>
                    <button className={styles.action} onClick={() => kick(false)}>Kick</button>
                    <button className={styles.danger} onClick={() => setConfirmBan(true)}>Ban</button>
                    <button className={styles.action} onClick={close}>✕</button>
                </>
            )}
        </div>
    );
}

// Shown to a player the host kicked or banned, until dismissed.
export const DisconnectNotice = () => {
    const notice = useValue(noticeBinding);
    if (!notice) {
        return null;
    }

    return (
        <div className={styles.notice_box}>
            <span>{notice}</span>
            <button className={styles.action} onClick={() => trigger(mod.id, "DismissDisconnectNotice")}>OK</button>
        </div>
    );
}
