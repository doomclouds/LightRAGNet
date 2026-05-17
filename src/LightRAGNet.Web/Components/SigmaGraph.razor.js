// Sigma.js wrapper for Blazor
let sigmaInstances = new Map();
let sigmaLoaded = false;

export function initializeSigma(containerId, graphDataJson, dotNetRef) {
    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`Container ${containerId} not found`);
        return;
    }

    // Load sigma.js dynamically if not already loaded
    if (!sigmaLoaded) {
        loadSigmaJS().then(() => {
            sigmaLoaded = true;
            createSigmaInstance(containerId, graphDataJson, dotNetRef);
        }).catch(err => {
            console.error('Failed to load sigma.js:', err);
        });
    } else {
        createSigmaInstance(containerId, graphDataJson, dotNetRef);
    }
}

function loadSigmaJS() {
    return new Promise((resolve, reject) => {
        // Check if already loading
        if (window.sigmaLoading) {
            window.sigmaLoading.then(resolve).catch(reject);
            return;
        }

        // Create loading promise
        window.sigmaLoading = new Promise((loadResolve, loadReject) => {
            // Load graphology first (using UMD build for browser compatibility)
            const graphologyScript = document.createElement('script');
            graphologyScript.src = 'https://cdn.jsdelivr.net/npm/graphology@0.26.0/dist/graphology.umd.min.js';
            graphologyScript.onerror = (e) => {
                console.error('Failed to load graphology:', e);
                loadReject(new Error('Failed to load graphology'));
            };
            graphologyScript.onload = () => {
                // Load graphology-layout-forceatlas2 (try index.js as it doesn't have min.js)
                const forceatlas2Script = document.createElement('script');
                // Try unpkg first
                forceatlas2Script.src = 'https://unpkg.com/graphology-layout-forceatlas2@0.10.1/index.js';
                forceatlas2Script.onerror = (e) => {
                    console.warn('Failed to load graphology-layout-forceatlas2 from unpkg, trying jsdelivr...');
                    // Fallback to jsdelivr
                    const fallbackScript = document.createElement('script');
                    fallbackScript.src = 'https://cdn.jsdelivr.net/npm/graphology-layout-forceatlas2@0.10.1/index.js';
                    fallbackScript.onerror = (e2) => {
                        console.warn('ForceAtlas2 layout not available, continuing without it');
                        // Continue without ForceAtlas2 - it's optional, load sigma.js directly
                        loadSigmaJSLibrary(loadResolve, loadReject);
                    };
                            fallbackScript.onload = () => {
                                loadSigmaJSLibrary(loadResolve, loadReject);
                            };
                    document.head.appendChild(fallbackScript);
                };
                forceatlas2Script.onload = () => {
                    loadSigmaJSLibrary(loadResolve, loadReject);
                };
                document.head.appendChild(forceatlas2Script);
            };
            document.head.appendChild(graphologyScript);
        });

        window.sigmaLoading.then(resolve).catch(reject);
    });
}

function loadSigmaJSLibrary(loadResolve, loadReject) {
    // Load sigma.js (try multiple CDN sources)
    const sigmaScript = document.createElement('script');
    // Try unpkg first (more reliable for sigma.js 3.x)
    sigmaScript.src = 'https://unpkg.com/sigma@3.0.2/dist/sigma.min.js';
    sigmaScript.onerror = (e) => {
        console.warn('Failed to load sigma.js from unpkg, trying jsdelivr...');
        // Fallback to jsdelivr
        const fallbackScript = document.createElement('script');
        fallbackScript.src = 'https://cdn.jsdelivr.net/npm/sigma@3.0.2/dist/sigma.min.js';
        fallbackScript.onerror = (e2) => {
            console.error('Failed to load sigma.js from both CDNs');
            console.error('Unpkg URL:', sigmaScript.src);
            console.error('jsDelivr URL:', fallbackScript.src);
            loadReject(new Error('Failed to load sigma.js from all CDN sources'));
        };
        fallbackScript.onload = () => {
            loadResolve();
        };
        document.head.appendChild(fallbackScript);
    };
    sigmaScript.onload = () => {
        loadResolve();
    };
    document.head.appendChild(sigmaScript);
}

function createSigmaInstance(containerId, graphDataJson, dotNetRef) {
    try {
        const graphData = JSON.parse(graphDataJson);
        
        // Handle both camelCase (from JSON) and PascalCase (from C#)
        const nodes = graphData.nodes || graphData.Nodes || [];
        const edges = graphData.edges || graphData.Edges || [];
        
        if (!nodes || nodes.length === 0) {
            console.warn('No nodes in graph data');
            return;
        }
        
        
        // Create graphology graph
        // UMD build exposes graphology as window.graphology
        const Graph = window.graphology || (typeof graphology !== 'undefined' ? graphology : null);
        if (!Graph) {
            console.error('Graphology not loaded');
            return;
        }
        const graph = new Graph();
        
        // First pass: calculate node degrees (number of connections)
        const nodeDegrees = new Map();
        edges.forEach(edge => {
            const source = edge.source || edge.Source;
            const target = edge.target || edge.Target;
            if (source && target) {
                nodeDegrees.set(source, (nodeDegrees.get(source) || 0) + 1);
                nodeDegrees.set(target, (nodeDegrees.get(target) || 0) + 1);
            }
        });
        
        // Add nodes with size based on degree (more connections = larger node)
        // Use better initial positioning - spread nodes in a circle pattern to avoid clustering
        const totalNodes = nodes.length;
        const baseRadius = Math.max(60, Math.sqrt(totalNodes) * 25); // Scale radius with node count for better separation
        
        nodes.forEach((node, index) => {
            const nodeId = node.id || node.Id;
            const degree = nodeDegrees.get(nodeId) || 1;
            // Calculate size: base size + degree multiplier
            // Nodes with more connections will be larger
            const baseSize = 8;
            const maxSize = 35;
            const size = Math.max(baseSize, Math.min(maxSize, baseSize + Math.sqrt(degree) * 4));
            
            // Get color from node data (should be hex format like #FF6B6B or #999)
            const nodeColor = node.color || node.Color;
            // Ensure color is valid hex format (supports both 3-digit and 6-digit hex)
            // Convert 3-digit hex to 6-digit if needed
            let finalColor = '#666666'; // Default color
            if (nodeColor) {
                // Check if it's a valid 6-digit hex color
                if (/^#[0-9A-Fa-f]{6}$/i.test(nodeColor)) {
                    finalColor = nodeColor;
                }
                // Check if it's a valid 3-digit hex color and convert to 6-digit
                else if (/^#[0-9A-Fa-f]{3}$/i.test(nodeColor)) {
                    // Convert #RGB to #RRGGBB
                    finalColor = '#' + nodeColor[1] + nodeColor[1] + nodeColor[2] + nodeColor[2] + nodeColor[3] + nodeColor[3];
                }
                // If it's already a valid color string, use it
                else if (nodeColor.startsWith('#')) {
                    finalColor = nodeColor;
                }
            }
            
            // Use circular distribution for initial positions to avoid clustering
            // Spread nodes evenly in a circle with some randomness
            const angle = (index / totalNodes) * Math.PI * 2;
            const radiusVariation = 0.4; // 40% variation in radius for better distribution
            const distance = baseRadius * (0.6 + Math.random() * radiusVariation);
            const x = Math.cos(angle) * distance;
            const y = Math.sin(angle) * distance;
            
            graph.addNode(nodeId, {
                label: node.label || node.Label,
                size: size,
                color: finalColor,
                x: x,
                y: y,
                ...(node.properties || node.Properties || {})
            });
        });
        
        // Add edges
        edges.forEach(edge => {
            const source = edge.source || edge.Source;
            const target = edge.target || edge.Target;
            if (graph.hasNode(source) && graph.hasNode(target)) {
                // Use 'line' type instead of 'DIRECTED' as sigma.js doesn't support custom edge types by default
                const edgeType = edge.type || edge.Type || 'line';
                const normalizedType = edgeType === 'DIRECTED' ? 'line' : edgeType;
                graph.addEdge(source, target, {
                    size: edge.size || edge.Size || 1.0, // Thinner edges for cleaner look
                    color: edge.color || edge.Color || '#CCCCCC', // Lighter edge color for white background
                    type: normalizedType
                });
            }
        });
        
        // Create sigma instance
        // UMD build exposes sigma as window.Sigma or window.sigma
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Container not found:', containerId);
            return;
        }
        
        // Check for sigma in different possible locations
        let Sigma = null;
        if (typeof window !== 'undefined') {
            Sigma = window.Sigma || window.sigma;
        }
        if (!Sigma && typeof sigma !== 'undefined') {
            Sigma = sigma;
        }
        
        if (!Sigma) {
            console.error('Sigma not loaded. Checking window object...');
            console.error('window.Sigma:', window.Sigma);
            console.error('window.sigma:', window.sigma);
            console.error('Available on window:', Object.keys(window).filter(k => k.toLowerCase().includes('sigma')));
            return;
        }
        
        // Sigma.js 3.x API: new Sigma(graph, container, settings)
        const sigmaInstance = new Sigma(graph, container, {
            minCameraRatio: 0.1,
            maxCameraRatio: 10,
            defaultNodeColor: '#666666',
            defaultEdgeColor: '#CCCCCC', // Lighter edge color for better visibility on white background
            labelFont: 'Arial',
            labelSize: 12,
            labelWeight: 'normal',
            labelColor: { attribute: 'color' },
            nodeLabelSize: 12,
            edgeLabelSize: 10,
            // Improve rendering quality
            renderLabels: true,
            zIndex: true
        });
        
        // Store instance
        sigmaInstances.set(containerId, sigmaInstance);
        
        // Add click event - only click to view node information
        sigmaInstance.on('clickNode', (e) => {
            const node = graph.getNodeAttributes(e.node);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('HandleNodeClick', e.node, JSON.stringify(node));
            }
        });
        
        // Start ForceAtlas2 layout (if available)
        // ForceAtlas2 may be available as a global function or via graphology.layout
        try {
            // Try different ways to access ForceAtlas2
            let forceAtlas2 = null;
            if (typeof window.forceAtlas2 !== 'undefined') {
                forceAtlas2 = window.forceAtlas2;
            } else if (window.graphology && window.graphology.layout && window.graphology.layout.forceAtlas2) {
                forceAtlas2 = window.graphology.layout.forceAtlas2;
            }
            
            if (forceAtlas2 && typeof forceAtlas2.assign === 'function') {
                // Use assign method (graphology-layout-forceatlas2 API)
                // Optimized settings for better node separation and visual clarity
                const nodeCount = graph.order;
                // Run layout synchronously and stop after iterations complete
                // This prevents continuous layout updates that could cause content to disappear
                forceAtlas2.assign(graph, {
                    iterations: Math.min(250, Math.max(200, nodeCount * 3)), // More iterations for better layout
                    settings: {
                        gravity: 0.05, // Very low gravity to allow maximum spread and prevent clustering
                        scalingRatio: Math.max(20, Math.min(40, nodeCount * 0.8)), // Higher scaling to push nodes far apart
                        strongGravityMode: false,
                        outboundAttractionDistribution: false, // Better for unweighted graphs
                        linLogMode: false,
                        adjustSizes: false, // Don't adjust sizes during layout
                        edgeWeightInfluence: 0, // Don't use edge weights
                        // Additional settings for better separation
                        barnesHutOptimize: true,
                        barnesHutTheta: 0.5,
                        slowDown: 1 // Normal speed
                    }
                });
                
                // Force stop any continuous layout after assign completes
                // This ensures layout doesn't keep running and causing content to disappear
                if (forceAtlas2.stop && typeof forceAtlas2.stop === 'function') {
                    setTimeout(() => {
                        try {
                            forceAtlas2.stop(graph);
                        } catch (e) {
                            // Ignore if stop is not available or fails
                        }
                    }, 100);
                }
            } else {
                // Use better distributed positions if ForceAtlas2 is not available
                // Spread nodes in a circle pattern to avoid clustering
                const nodeCount = graph.order;
                const radius = Math.max(50, Math.sqrt(nodeCount) * 20);
                let nodeIndex = 0;
                graph.forEachNode((node) => {
                    const angle = (nodeIndex / nodeCount) * Math.PI * 2;
                    const distance = radius * (0.7 + Math.random() * 0.3);
                    graph.setNodeAttribute(node, 'x', Math.cos(angle) * distance);
                    graph.setNodeAttribute(node, 'y', Math.sin(angle) * distance);
                    nodeIndex++;
                });
            }
        } catch (e) {
            console.warn('Error applying layout:', e);
            // Fallback to better distributed positions
            const nodeCount = graph.order;
            const radius = Math.max(50, Math.sqrt(nodeCount) * 20);
            let nodeIndex = 0;
            graph.forEachNode((node) => {
                const angle = (nodeIndex / nodeCount) * Math.PI * 2;
                const distance = radius * (0.7 + Math.random() * 0.3);
                graph.setNodeAttribute(node, 'x', Math.cos(angle) * distance);
                graph.setNodeAttribute(node, 'y', Math.sin(angle) * distance);
                nodeIndex++;
            });
        }
        
        // Refresh sigma
        sigmaInstance.refresh();
        
        // Reset camera to fit all nodes using Sigma.js built-in animatedReset
        // This automatically calculates the best view to fit all nodes
        const resetKey = `cameraReset_${containerId}`;
        if (!window[resetKey]) {
            window[resetKey] = true;
            setTimeout(() => {
                try {
                    const camera = sigmaInstance.getCamera();
                    // Use Sigma.js built-in animatedReset which automatically fits all nodes
                    camera.animatedReset({ duration: 500 });
                } catch (e) {
                    console.warn('Error resetting camera:', e);
                }
                window[resetKey] = 'initialized';
            }, 1000); // Wait for layout to fully stabilize
        }
    } catch (error) {
        console.error('Error initializing sigma:', error);
    }
}

export function updateSigmaGraph(containerId, graphDataJson) {
    const sigmaInstance = sigmaInstances.get(containerId);
    if (!sigmaInstance) {
        console.error(`Sigma instance ${containerId} not found`);
        return;
    }
    
    try {
        const graphData = JSON.parse(graphDataJson);
        // Handle both camelCase (from JSON) and PascalCase (from C#)
        const nodes = graphData.nodes || graphData.Nodes || [];
        const edges = graphData.edges || graphData.Edges || [];
        const graph = sigmaInstance.graph;
        
        // Save current camera state to preserve user's view
        const camera = sigmaInstance.getCamera();
        const savedCameraState = camera.getState();
        
        // Save current node positions before clearing
        const savedPositions = new Map();
        graph.forEachNode((node) => {
            savedPositions.set(node, {
                x: graph.getNodeAttribute(node, 'x'),
                y: graph.getNodeAttribute(node, 'y')
            });
        });
        
        // Clear existing graph
        graph.clear();
        
        // Calculate node degrees first
        const nodeDegrees = new Map();
        edges.forEach(edge => {
            const source = edge.source || edge.Source;
            const target = edge.target || edge.Target;
            if (source && target) {
                nodeDegrees.set(source, (nodeDegrees.get(source) || 0) + 1);
                nodeDegrees.set(target, (nodeDegrees.get(target) || 0) + 1);
            }
        });
        
        // Add nodes - restore positions if they exist, otherwise use random
        nodes.forEach(node => {
            const nodeId = node.id || node.Id;
            const degree = nodeDegrees.get(nodeId) || 1;
            const baseSize = 8;
            const maxSize = 35;
            const size = Math.max(baseSize, Math.min(maxSize, baseSize + Math.sqrt(degree) * 4));
            
            // Get color from node data (should be hex format like #FF6B6B or #999)
            const nodeColor = node.color || node.Color;
            // Ensure color is valid hex format (supports both 3-digit and 6-digit hex)
            let finalColor = '#666666'; // Default color
            if (nodeColor) {
                // Check if it's a valid 6-digit hex color
                if (/^#[0-9A-Fa-f]{6}$/i.test(nodeColor)) {
                    finalColor = nodeColor;
                }
                // Check if it's a valid 3-digit hex color and convert to 6-digit
                else if (/^#[0-9A-Fa-f]{3}$/i.test(nodeColor)) {
                    // Convert #RGB to #RRGGBB
                    finalColor = '#' + nodeColor[1] + nodeColor[1] + nodeColor[2] + nodeColor[2] + nodeColor[3] + nodeColor[3];
                }
                // If it's already a valid color string, use it
                else if (nodeColor.startsWith('#')) {
                    finalColor = nodeColor;
                }
            }
            
            // Restore position if it existed, otherwise use random
            const savedPos = savedPositions.get(nodeId);
            const x = savedPos ? savedPos.x : Math.random() * 100;
            const y = savedPos ? savedPos.y : Math.random() * 100;
            
            graph.addNode(nodeId, {
                label: node.label || node.Label,
                size: size,
                color: finalColor,
                x: x,
                y: y,
                ...(node.properties || node.Properties || {})
            });
        });
        
        // Add edges
        edges.forEach(edge => {
            const source = edge.source || edge.Source;
            const target = edge.target || edge.Target;
            if (graph.hasNode(source) && graph.hasNode(target)) {
                // Use 'line' type instead of 'DIRECTED' as sigma.js doesn't support custom edge types by default
                const edgeType = edge.type || edge.Type || 'line';
                const normalizedType = edgeType === 'DIRECTED' ? 'line' : edgeType;
                graph.addEdge(source, target, {
                    size: edge.size || edge.Size || 1.0, // Thinner edges for cleaner look
                    color: edge.color || edge.Color || '#CCCCCC', // Lighter edge color for white background
                    type: normalizedType
                });
            }
        });
        
        // Refresh sigma
        sigmaInstance.refresh();
        
        // Restore camera state to preserve user's view position and zoom
        // This prevents camera from resetting when graph is updated via Load button
        try {
            camera.setState(savedCameraState);
            sigmaInstance.refresh();
        } catch (e) {
            console.warn('Error restoring camera state:', e);
        }
    } catch (error) {
        console.error('Error updating sigma graph:', error);
    }
}

export function zoomIn(containerId) {
    const sigmaInstance = sigmaInstances.get(containerId);
    if (sigmaInstance) {
        const camera = sigmaInstance.getCamera();
        camera.animatedZoom({ duration: 300, factor: 1.5 });
    }
}

export function zoomOut(containerId) {
    const sigmaInstance = sigmaInstances.get(containerId);
    if (sigmaInstance) {
        const camera = sigmaInstance.getCamera();
        camera.animatedZoom({ duration: 300, factor: 0.67 });
    }
}


export function resetCamera(containerId) {
    const sigmaInstance = sigmaInstances.get(containerId);
    if (!sigmaInstance) {
        console.warn(`Sigma instance ${containerId} not found for resetCamera`);
        return;
    }
    
    try {
        const camera = sigmaInstance.getCamera();
        // Use Sigma.js built-in animatedReset which automatically fits all nodes
        camera.animatedReset({ duration: 500 });
    } catch (e) {
        console.error('Error resetting camera:', e);
    }
}

export function disposeSigma(containerId) {
    const sigmaInstance = sigmaInstances.get(containerId);
    if (sigmaInstance) {
        sigmaInstance.kill();
        sigmaInstances.delete(containerId);
    }
}
