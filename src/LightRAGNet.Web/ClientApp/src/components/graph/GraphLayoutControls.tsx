import { useSigma } from "@react-sigma/core";
import { useLayoutCircular } from "@react-sigma/layout-circular";
import { useLayoutForce } from "@react-sigma/layout-force";
import { useLayoutForceAtlas2 } from "@react-sigma/layout-forceatlas2";
import { useLayoutNoverlap } from "@react-sigma/layout-noverlap";
import { useLayoutRandom } from "@react-sigma/layout-random";
import { Grip, Network, Shuffle } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { animateNodes } from "sigma/utils";

import { useGraphSettingsStore } from "../../stores/graphSettingsStore";

type LayoutName = "Force Atlas" | "Force Directed" | "Noverlap" | "Random" | "Circular";

export function GraphLayoutControls() {
  const sigma = useSigma();
  const [isOpen, setIsOpen] = useState(false);
  const layoutIterations = useGraphSettingsStore((state) => state.layoutIterations);
  const layoutCircular = useLayoutCircular();
  const layoutRandom = useLayoutRandom();
  const layoutNoverlap = useLayoutNoverlap({
    maxIterations: layoutIterations,
    settings: {
      margin: 6,
      expansion: 1.15,
      gridSize: 1,
      ratio: 1,
      speed: 3
    }
  });
  const layoutForce = useLayoutForce({
    maxIterations: layoutIterations,
    settings: {
      attraction: 0.0003,
      repulsion: 0.02,
      gravity: 0.02,
      inertia: 0.4,
      maxMove: 100
    }
  });
  const layoutForceAtlas = useLayoutForceAtlas2({
    iterations: layoutIterations,
    settings: {
      barnesHutOptimize: sigma.getGraph().order > 60,
      edgeWeightInfluence: 0.7,
      gravity: 0.04,
      linLogMode: true,
      scalingRatio: 28,
      slowDown: 2
    }
  });

  const layouts = useMemo(
    () => ({
      "Force Atlas": layoutForceAtlas,
      "Force Directed": layoutForce,
      Noverlap: layoutNoverlap,
      Random: layoutRandom,
      Circular: layoutCircular
    }),
    [layoutCircular, layoutForce, layoutForceAtlas, layoutNoverlap, layoutRandom]
  );

  const runLayout = useCallback(
    (name: LayoutName) => {
      const graph = sigma.getGraph();
      const positions = layouts[name].positions();
      animateNodes(graph, positions, { duration: 420 });
      setIsOpen(false);
    },
    [layouts, sigma]
  );

  return (
    <div className="graph-workbench__layout-control">
      <button
        className="graph-workbench__icon-button graph-workbench__icon-button--primary"
        title="Layout graph"
        type="button"
        onClick={() => setIsOpen((value) => !value)}
      >
        <Grip aria-hidden="true" size={17} />
      </button>
      {isOpen ? (
        <div className="graph-workbench__layout-menu">
          {(Object.keys(layouts) as LayoutName[]).map((name) => (
            <button key={name} type="button" onClick={() => runLayout(name)}>
              {name === "Random" ? <Shuffle aria-hidden="true" size={14} /> : <Network aria-hidden="true" size={14} />}
              <span>{name}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
