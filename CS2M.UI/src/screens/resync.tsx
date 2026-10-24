import {bindValue, trigger, useValue} from "cs2/api";
import mod from "../../mod.json";
import styles from "./resync.module.scss";

const statusBinding = bindValue<string>(mod.id, 'PlayerStatus', 'INACTIVE');
const doneBinding = bindValue<number>(mod.id, 'DownloadDone', 0);
const remainingBinding = bindValue<number>(mod.id, 'DownloadRemaining', 0);

// Re-downloads the host's city to clear drift. On the host it sends the city to every joiner again.
export const ResyncButton = ({host}: { host: boolean }) => (
    <button
        className={styles.button}
        title={host ? "Send the city to everyone again" : "Download the host's city again"}
        onClick={() => trigger(mod.id, "Resync")}>
        {host ? "Resync all" : "Resync"}
    </button>
);

// While a resync downloads or loads, the HUD has no session to show; this says what's happening instead.
// (Worded for the first join too: the game UI can mount while that one is still loading.)
export const ResyncStatus = () => {
    const status = useValue(statusBinding);
    const done = useValue(doneBinding);
    const remaining = useValue(remainingBinding);
    if (status !== "DOWNLOADING_MAP" && status !== "LOADING_MAP") {
        return null;
    }

    const total = done + remaining;
    const text = status === "LOADING_MAP"
        ? "Loading the host's city…"
        : `Downloading the host's city${total > 0 ? ` ${Math.floor(done * 100 / total)}%` : "…"}`;
    return <div className={styles.status}>{text}</div>;
}
