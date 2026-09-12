const canvas = document.getElementById('arena');
const ctx = canvas.getContext('2d');
let width, height;

function resize() {
    width = window.innerWidth;
    height = window.innerHeight;
    canvas.width = width;
    canvas.height = height;
}
window.addEventListener('resize', resize);
resize();

let state = { nodes: [], messages: [], round: 0, consensus: '-', blocks: [], metrics: {} };
let isSplit = false;
let selectedNodeId = null;

let ws;
let reconnectTimeout = 1000;

function connect() {
    ws = new WebSocket('ws://localhost:8765');

    ws.onopen = () => {
        document.getElementById('status').innerText = 'Connected';
        document.getElementById('status').className = 'connected';
        reconnectTimeout = 1000;
    };

    ws.onmessage = (event) => {
        try {
            state = JSON.parse(event.data);
            updateUI();
        } catch (e) {
            console.error('Failed to parse state:', e);
        }
    };

    ws.onclose = () => {
        document.getElementById('status').innerText = 'Disconnected';
        document.getElementById('status').className = '';
        setTimeout(connect, reconnectTimeout);
        reconnectTimeout = Math.min(reconnectTimeout * 2, 10000);
    };
}

function updateUI() {
    document.getElementById('round').innerText = state.round || 0;
    document.getElementById('consensus').innerText = state.consensus || '-';
    document.getElementById('nodeCount').innerText = state.nodes ? state.nodes.length : 0;
    document.getElementById('tps').innerText = (state.metrics && state.metrics.tps) || 0;

    // Update node dropdown (rebuild when count changes)
    const nodeSelect = document.getElementById('nodeSelect');
    if (state.nodes && nodeSelect.children.length !== state.nodes.length) {
        nodeSelect.innerHTML = '';
        state.nodes.forEach(node => {
            const opt = document.createElement('option');
            opt.value = node.id;
            opt.innerText = node.id;
            nodeSelect.appendChild(opt);
        });
    }

    // Leaderboard
    if (state.nodes) {
        const sorted = [...state.nodes].sort((a, b) => b.reputation - a.reputation);
        let html = '';
        sorted.forEach((n, i) => {
            let color = '#4caf50'; // Default Green
            if (n.status === 'Verified') color = '#0ff';
            else if (n.status === 'Trusted') color = '#4caf50';
            else if (n.status === 'Watched') color = '#ff9800';
            else if (n.status === 'High Risk') color = '#ff5722';
            else if (n.status === 'Quarantined') color = '#f44336';
            else if (n.status === 'Blacklisted') color = '#8b0000';
            
            html += `<div class="lb-row" style="color:${color}">${i+1}. ${n.id} — ${Math.round(n.reputation)}</div>`;
        });
        document.getElementById('lbContent').innerHTML = html;
    }

    // Block Ticker
    if (state.blocks && state.blocks.length > 0) {
        let ticker = state.blocks.map(b => `[${b.hash} | ${b.proposer} | Tx:${b.tx_count} | ${b.consensus}]`).join('     ');
        document.getElementById('ticker').innerText = ticker;
    }

    // Node detail panel
    if (selectedNodeId && state.nodes) {
        const n = state.nodes.find(x => x.id === selectedNodeId);
        if (n) showDetail(n);
    }
}

function showDetail(n) {
    const panel = document.getElementById('nodeDetail');
    panel.style.display = 'block';
    document.getElementById('detailTitle').innerText = n.id;
    document.getElementById('detailRep').innerText = n.reputation.toFixed(1);
    document.getElementById('detailStatus').innerText = n.status;
    document.getElementById('detailProfile').innerText = n.network_profile || "Unknown";
    
    let color = '#4caf50';
    if (n.status === 'Verified') color = '#0ff';
    else if (n.status === 'Trusted') color = '#4caf50';
    else if (n.status === 'Watched') color = '#ff9800';
    else if (n.status === 'High Risk') color = '#ff5722';
    else if (n.status === 'Quarantined') color = '#f44336';
    else if (n.status === 'Blacklisted') color = '#8b0000';
    
    document.getElementById('detailStatus').style.color = color;
    document.getElementById('detailThreat').innerText = (n.ml_prob * 100).toFixed(1) + '%';

    // ML Model Breakdown
    if (n.ml_detail) {
        document.getElementById('detailNN').innerText = ((n.ml_detail.nn_prob || 0) * 100).toFixed(1) + '%';
        document.getElementById('detailAnomaly').innerText = ((n.ml_detail.anomaly_score || 0) * 100).toFixed(1) + '%';
    }

    if (n.ml_features) {
        document.getElementById('detailFreq').innerText = (n.ml_features.msg_freq || 0).toFixed(1);
        document.getElementById('detailVote').innerText = ((n.ml_features.vote_inconsistency || 0) * 100).toFixed(1) + '%';
        document.getElementById('detailLat').innerText = (n.ml_features.latency || 0).toFixed(0) + ' ms';
        document.getElementById('detailForks').innerText = (n.ml_features.fork_attempts || 0).toFixed(0);
        document.getElementById('detailSilence').innerText = ((n.ml_features.silence_ratio || 0) * 100).toFixed(1) + '%';
    }
}

// ── Online Training Commands ──
function trainFalsePositive() {
    if (selectedNodeId && ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ action: 'report_false_positive', node_id: selectedNodeId }));
    }
}

function trainMissedAttack() {
    if (selectedNodeId && ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ action: 'report_missed_attack', node_id: selectedNodeId }));
    }
}

// ── Commands ──
function switchConsensus(consensus) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ action: 'switch_consensus', consensus }));
    }
}

function injectFault() {
    const nodeId = document.getElementById('nodeSelect').value;
    const faultType = document.getElementById('faultSelect').value;
    if (ws && ws.readyState === WebSocket.OPEN && nodeId) {
        ws.send(JSON.stringify({ action: 'inject_fault', node_id: nodeId, fault_type: faultType }));
    }
}

function toggleSplit() {
    isSplit = !isSplit;
    const btn = document.getElementById('splitBtn');
    if (isSplit) {
        sendAction('split_network');
        btn.innerText = 'Merge Network';
        btn.classList.add('active');
    } else {
        sendAction('merge_network');
        btn.innerText = 'Sever Network';
        btn.classList.remove('active');
    }
}

function sendAction(action) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ action }));
    }
}

// ── Canvas Rendering ──
function render() {
    ctx.fillStyle = '#111';
    ctx.fillRect(0, 0, width, height);

    const centerX = width / 2;
    const centerY = height / 2;
    const baseRadius = Math.min(width, height) * 0.3;

    const nodePositions = {};

    if (state.nodes && state.nodes.length > 0) {
        // Check if partitioned
        const partitions = {};
        state.nodes.forEach(n => {
            const pid = n.partition_id || 0;
            if (!partitions[pid]) partitions[pid] = [];
            partitions[pid].push(n);
        });
        const partitionIds = Object.keys(partitions);
        const isPartitioned = partitionIds.length > 1;

        partitionIds.forEach((pid, pIdx) => {
            const pNodes = partitions[pid];
            const angleStep = (Math.PI * 2) / pNodes.length;
            let offsetX = 0;
            let r = baseRadius;

            if (isPartitioned) {
                r = baseRadius * 0.6;
                offsetX = pIdx === 0 ? -r * 1.2 : r * 1.2;
            }

            // Adjust radius for node count
            r = Math.max(r, pNodes.length * 8);

            pNodes.forEach((node, i) => {
                const angle = i * angleStep - Math.PI / 2;
                const x = centerX + offsetX + Math.cos(angle) * r;
                const y = centerY + Math.sin(angle) * r;
                nodePositions[node.id] = { x, y };

                // Node circle
                ctx.beginPath();
                ctx.arc(x, y, 18, 0, Math.PI * 2);

                // Color coding
                if (node.status === 'Verified') {
                    ctx.fillStyle = '#0ff'; // Cyan
                } else if (node.status === 'Trusted') {
                    ctx.fillStyle = '#4caf50'; // Green
                } else if (node.status === 'Watched') {
                    ctx.fillStyle = '#ff9800'; // Orange
                } else if (node.status === 'High Risk') {
                    ctx.fillStyle = '#ff5722'; // Deep Orange
                } else if (node.status === 'Quarantined') {
                    ctx.fillStyle = '#f44336'; // Red
                } else if (node.status === 'Blacklisted') {
                    ctx.fillStyle = '#8b0000'; // Dark Red
                } else {
                    ctx.fillStyle = '#4caf50'; // Default Green
                }
                ctx.fill();

                if (node.is_byzantine) {
                    ctx.lineWidth = 3;
                    if (node.fault_type === 'stealth') {
                        if (node.reputation >= 55) {
                            // Actively attacking
                            ctx.strokeStyle = `rgba(255, 0, 0, ${0.5 + Math.sin(Date.now()/100)*0.5})`; // Fast red pulse
                            ctx.fillStyle = '#ff4444';
                            ctx.font = 'bold 10px monospace';
                            ctx.fillText("⚔️ ATTACKING", x, y - 40);
                        } else {
                            // Hiding / acting innocent
                            ctx.strokeStyle = `rgba(100, 100, 255, ${0.3 + Math.sin(Date.now()/500)*0.3})`; // Slow blue pulse
                            ctx.fillStyle = '#8888ff';
                            ctx.font = 'bold 10px monospace';
                            ctx.fillText("👻 HIDING", x, y - 40);
                        }
                    } else {
                        // Normal malicious/offline
                        ctx.strokeStyle = `rgba(255,0,0,${0.5 + Math.sin(Date.now()/200)*0.5})`;
                    }
                } else {
                    ctx.lineWidth = 2;
                    ctx.strokeStyle = '#fff';
                }
                ctx.stroke();

                // Label
                ctx.fillStyle = '#fff';
                ctx.font = '11px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(node.id, x, y - 26);
                ctx.fillText(`Rep: ${Math.round(node.reputation)}`, x, y + 32);

                // ML threat bar
                const threatW = 30;
                const threatH = 4;
                const threat = node.ml_prob || 0;
                ctx.fillStyle = '#333';
                ctx.fillRect(x - threatW/2, y + 36, threatW, threatH);
                ctx.fillStyle = threat > 0.7 ? '#f33' : (threat > 0.4 ? '#ff0' : '#0f0');
                ctx.fillRect(x - threatW/2, y + 36, threatW * threat, threatH);
            });
        });

        // Draw partition divider
        if (isPartitioned) {
            ctx.beginPath();
            ctx.setLineDash([10, 10]);
            ctx.moveTo(centerX, 50);
            ctx.lineTo(centerX, height - 50);
            ctx.strokeStyle = '#f33';
            ctx.lineWidth = 2;
            ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle = '#f33';
            ctx.font = '14px monospace';
            ctx.textAlign = 'center';
            ctx.fillText('NETWORK SEVERED', centerX, 40);
        }

        // Draw messages
        if (state.messages) {
            state.messages.forEach(msg => {
                const p1 = nodePositions[msg.from];
                const p2 = nodePositions[msg.to];
                if (p1 && p2) {
                    ctx.beginPath();
                    ctx.moveTo(p1.x, p1.y);
                    ctx.lineTo(p2.x, p2.y);
                    ctx.strokeStyle = 'rgba(0,255,255,0.3)';
                    ctx.lineWidth = 1;
                    ctx.stroke();

                    const progress = (Date.now() % 800) / 800;
                    const mx = p1.x + (p2.x - p1.x) * progress;
                    const my = p1.y + (p2.y - p1.y) * progress;
                    ctx.beginPath();
                    ctx.arc(mx, my, 3, 0, Math.PI * 2);
                    ctx.fillStyle = '#0ff';
                    ctx.fill();
                }
            });
        }
    }

    requestAnimationFrame(render);
}

// Click on canvas to select a node
canvas.addEventListener('click', (e) => {
    if (!state.nodes) return;
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;

    // Check which node was clicked (simple distance check)
    for (const node of state.nodes) {
        const centerX = width / 2;
        const centerY = height / 2;
        const baseRadius = Math.min(width, height) * 0.3;

        // Recompute positions (same logic as render)
        const partitions = {};
        state.nodes.forEach(n => {
            const pid = n.partition_id || 0;
            if (!partitions[pid]) partitions[pid] = [];
            partitions[pid].push(n);
        });
        const partitionIds = Object.keys(partitions);
        const isPartitioned = partitionIds.length > 1;

        for (const pid of partitionIds) {
            const pNodes = partitions[pid];
            const pIdx = partitionIds.indexOf(pid);
            const angleStep = (Math.PI * 2) / pNodes.length;
            let offsetX = 0, r = baseRadius;
            if (isPartitioned) { r = baseRadius * 0.6; offsetX = pIdx === 0 ? -r*1.2 : r*1.2; }
            r = Math.max(r, pNodes.length * 8);

            pNodes.forEach((n, i) => {
                const angle = i * angleStep - Math.PI / 2;
                const x = centerX + offsetX + Math.cos(angle) * r;
                const y = centerY + Math.sin(angle) * r;
                const dist = Math.sqrt((mx-x)**2 + (my-y)**2);
                if (dist < 20) {
                    selectedNodeId = n.id;
                    showDetail(n);
                }
            });
        }
        break; // Only need one pass
    }
});

connect();
render();
