import { Maximize2, Minimize2 } from "lucide-react";
import { useEffect, useState } from "react";

export function GraphFullscreenControl() {
  const [isFullscreen, setIsFullscreen] = useState(false);

  useEffect(() => {
    const onChange = () => setIsFullscreen(document.fullscreenElement !== null);
    document.addEventListener("fullscreenchange", onChange);
    return () => document.removeEventListener("fullscreenchange", onChange);
  }, []);

  async function toggleFullscreen() {
    if (document.fullscreenElement) {
      await document.exitFullscreen();
      return;
    }

    const root = document.querySelector(".graph-workbench");
    if (root instanceof HTMLElement) {
      await root.requestFullscreen();
    }
  }

  return (
    <button
      className="graph-workbench__icon-button"
      title={isFullscreen ? "Exit fullscreen" : "Fullscreen"}
      type="button"
      onClick={() => void toggleFullscreen()}
    >
      {isFullscreen ? <Minimize2 aria-hidden="true" size={17} /> : <Maximize2 aria-hidden="true" size={17} />}
    </button>
  );
}
