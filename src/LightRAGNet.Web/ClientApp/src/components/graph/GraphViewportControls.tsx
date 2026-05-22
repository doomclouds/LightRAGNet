import { useCamera, useSigma } from "@react-sigma/core";
import { LocateFixed, RotateCcw, RotateCw, ZoomIn, ZoomOut } from "lucide-react";
import { useCallback } from "react";

type GraphViewportControlsProps = {
  legendVisible: boolean;
  onToggleLegend: () => void;
};

export function GraphViewportControls({ legendVisible, onToggleLegend }: GraphViewportControlsProps) {
  const { zoomIn, zoomOut, reset } = useCamera({ duration: 220, factor: 1.5 });
  const sigma = useSigma();

  const rotate = useCallback(
    (direction: 1 | -1) => {
      const camera = sigma.getCamera();
      camera.animate({ angle: camera.angle + direction * (Math.PI / 8) }, { duration: 220 });
    },
    [sigma]
  );

  const resetView = useCallback(() => {
    sigma.setCustomBBox(null);
    sigma.refresh();
    reset();
  }, [reset, sigma]);

  return (
    <>
      <button className="graph-workbench__icon-button" title="Rotate clockwise" type="button" onClick={() => rotate(1)}>
        <RotateCw aria-hidden="true" size={17} />
      </button>
      <button className="graph-workbench__icon-button" title="Rotate counter-clockwise" type="button" onClick={() => rotate(-1)}>
        <RotateCcw aria-hidden="true" size={17} />
      </button>
      <button className="graph-workbench__icon-button" title="Reset view" type="button" onClick={resetView}>
        <LocateFixed aria-hidden="true" size={17} />
      </button>
      <button className="graph-workbench__icon-button" title="Zoom in" type="button" onClick={() => zoomIn()}>
        <ZoomIn aria-hidden="true" size={17} />
      </button>
      <button className="graph-workbench__icon-button" title="Zoom out" type="button" onClick={() => zoomOut()}>
        <ZoomOut aria-hidden="true" size={17} />
      </button>
      <button
        className={`graph-workbench__icon-button${legendVisible ? " graph-workbench__icon-button--active" : ""}`}
        title="Toggle legend"
        type="button"
        onClick={onToggleLegend}
      >
        <span aria-hidden="true" className="graph-workbench__legend-icon" />
      </button>
    </>
  );
}
