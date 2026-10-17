import { clamp, cellAt, castRay, validSnapshot, makeDemo, TAU, InputBuffer } from './core.mjs';

const $ = id => document.getElementById(id);
const canvas = $('scene'), ctx = canvas.getContext('2d', {alpha:false});
const demo = new URLSearchParams(location.search).get('demo') === '1';
const token = new URLSearchParams(location.hash.slice(1)).get('token') || '';
let world = demo ? makeDemo() : null;
let yaw = 0, seq = 0, locked = false, connected = demo, lastState = 0, nextPoll = 0;
let keyboardMode = false, captureMessage = '';
let pollBusy = false, inputBusy = false, lastInput = 0;
let releasePending = false, connectionMessage = 'Waiting for local bridge', lastDemoShot = 0, muzzle = 0;
let lastFrame = performance.now(), demoReloadUntil = 0;
const controls = new InputBuffer(), keys = controls.keys;
document.body.classList.toggle('demo', demo);
if (demo) { $('demo-link').textContent = 'Return to the live bridge →'; $('demo-link').href = './'; }
canvas.width = 640; canvas.height = 360;
const depth = new Float32Array(canvas.width);
const self = () => world?.pawns.find(p => p.id === world.controlledPawnId);
const ready = () => !!(connected && world?.active && !world.paused && self() && !self().downed);
const engaged = () => locked || keyboardMode;
const active = () => engaged() && document.hasFocus() && ready();
function clearInput() { controls.clear(); }
function release() {
  clearInput();
  if (!demo) { releasePending = true; void sendInput(); }
}
function disengage() {
  const wasEngaged=engaged();
  keyboardMode=false;
  // Merely opening the client and returning to RimWorld must not cancel possession.
  if(wasEngaged) release(); else clearInput();
  if(locked) document.exitPointerLock();
}
async function api(path, options = {}) {
  const response = await fetch(path, {...options, cache:'no-store', signal:AbortSignal.timeout(1800), headers:{'X-RimFront-Token':token, ...(options.headers || {})}});
  if (!response.ok) throw new Error(response.status === 401 || response.status === 403 ? 'Link token rejected. Open the client again from RimWorld.' : `Bridge returned HTTP ${response.status}`);
  return response;
}
async function poll() {
  if (demo || pollBusy) return;
  pollBusy = true;
  try {
    const state = await (await api('/api/state')).json();
    if (!validSnapshot(state)) {
      if (state?.protocol === 1 && !state.active) {
        world = null; connected = true; lastState = performance.now(); connectionMessage = state.message || state.status || 'Select a colonist and start a field link in RimWorld.';
        if (engaged()) disengage();
        return;
      }
      throw new Error('Unsupported or malformed bridge snapshot');
    }
    world = state; connected = true; lastState = performance.now(); connectionMessage = '';
    if (!ready() && engaged()) disengage();
  } catch (error) {
    connected = false; connectionMessage = error.name === 'TimeoutError' ? 'Bridge timed out. Reconnecting…' : error.message;
    if (engaged()) disengage();
  } finally { pollBusy = false; }
}
async function sendInput() {
  if (demo || inputBusy || (!releasePending && !active())) return;
  const releasing = releasePending;
  if (releasing) releasePending = false;
  const state = releasing ? {forward:0,strafe:0,yaw,fire:false,reload:false} : controls.take(yaw);
  const body = {seq:++seq,action:releasing?'release':'input',...state};
  inputBusy = true;
  try { await api('/api/input', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}); }
  catch (error) { connected = false; connectionMessage = error.message; if(engaged()) disengage(); }
  finally { inputBusy = false; if(releasePending) void sendInput(); }
}
$('enter').addEventListener('click', async () => {
  if (!ready()) return;
  captureMessage='Capturing mouse… If this browser blocks capture, use arrow-key look below.';
  try { await canvas.requestPointerLock(); } catch { captureMessage = 'Mouse capture is unavailable here. Use arrow-key look, or open the client in a desktop browser.'; }
});
$('keyboard-enter').addEventListener('click', () => {
  if(!ready()) return;
  clearInput(); keyboardMode=true; captureMessage='';
});
document.addEventListener('pointerlockchange', () => {
  const wasLocked=locked;
  locked = document.pointerLockElement === canvas;
  if(locked) { keyboardMode=false; captureMessage=''; }
  else if(wasLocked) release();
});
document.addEventListener('pointerlockerror', () => { captureMessage = 'Mouse capture was blocked. Use arrow-key look, or open the client in a desktop browser.'; });
window.addEventListener('blur', disengage);
document.addEventListener('visibilitychange', () => { if(document.hidden) disengage(); });
window.addEventListener('pagehide', disengage);
document.addEventListener('keydown', event => {
  if(!active()) return;
  if(event.code==='Escape' && keyboardMode) {disengage();return;}
  if(['KeyW','KeyA','KeyS','KeyD','KeyR','Space','ArrowLeft','ArrowRight'].includes(event.code)) event.preventDefault();
  keys.add(event.code);
  if(event.code === 'KeyR' && !event.repeat) controls.requestReload();
});
document.addEventListener('keyup', event => keys.delete(event.code));
document.addEventListener('mousemove', event => { if(active() && locked) yaw = ((yaw + event.movementX * .0025) % TAU + TAU) % TAU; });
document.addEventListener('mousedown', event => { if(event.button === 0 && active()) controls.pressFire(); });
document.addEventListener('mouseup', event => { if(event.button === 0) controls.releaseFire(); });
canvas.addEventListener('contextmenu', event => event.preventDefault());

function updateDemo(dt, now) {
  if (!active()) return;
  const p = self();
  const input = controls.take(yaw);
  let forward = input.forward, strafe = input.strafe;
  const norm = Math.max(1,Math.hypot(forward,strafe)); forward /= norm; strafe /= norm;
  const dx = (Math.sin(yaw)*forward + Math.cos(yaw)*strafe)*dt*3.1;
  const dz = (Math.cos(yaw)*forward - Math.sin(yaw)*strafe)*dt*3.1;
  const open = (x,z) => [[-.2,-.2],[.2,-.2],[-.2,.2],[.2,.2]].every(([a,b])=>[0,3].includes(cellAt(world,x+a,z+b)));
  if(open(p.x+dx,p.z)) p.x += dx;
  if(open(p.x,p.z+dz)) p.z += dz;
  if(input.reload && world.weapon.ammo < world.weapon.capacity && !demoReloadUntil) demoReloadUntil=now+1300;
  if(demoReloadUntil && now>=demoReloadUntil) { world.weapon.ammo=world.weapon.capacity; demoReloadUntil=0; }
  world.weapon.status=demoReloadUntil?'Reloading':world.weapon.ammo?'Ready':'Empty · R to reload';
  if(input.fire && !demoReloadUntil && world.weapon.ammo>0 && now-lastDemoShot>160) {
    lastDemoShot=now; muzzle=now+70; world.weapon.ammo--;
    const wall=castRay(world,p.x,p.z,Math.sin(yaw),Math.cos(yaw));
    const target=world.pawns.filter(q=>q.hostile&&!q.downed).map(q=>({q,d:Math.hypot(q.x-p.x,q.z-p.z),a:Math.atan2(q.x-p.x,q.z-p.z)})).filter(t=>Math.abs(Math.atan2(Math.sin(t.a-yaw),Math.cos(t.a-yaw)))<.30/t.d&&t.d<wall.distance).sort((a,b)=>a.d-b.d)[0];
    if(target) {target.q.health=Math.max(0,target.q.health-.25);target.q.downed=target.q.health===0;}
  }
  world.tick++;
}
function tint(rgb,factor){return `rgb(${rgb.map(v=>Math.round(v*factor)).join(',')})`;}
function render(now) {
  const W=canvas.width,H=canvas.height,horizon=H*.48, focal=W*.69;
  const sky=ctx.createLinearGradient(0,0,0,horizon);sky.addColorStop(0,'#263c41');sky.addColorStop(1,'#8a9990');ctx.fillStyle=sky;ctx.fillRect(0,0,W,horizon);
  ctx.fillStyle='#414b39';ctx.fillRect(0,horizon,W,H-horizon);
  const p=self();
  if(!world || !p) {ctx.fillStyle='#263c4155';for(let x=0;x<W;x+=40){ctx.fillRect(x,horizon,1,H);}return;}
  const sx=Math.sin(yaw),cz=Math.cos(yaw),px=Math.cos(yaw),pz=-Math.sin(yaw);
  // Low resolution ground pass gives each map cell a stable material without downloaded textures.
  for(let y=Math.ceil(horizon)+3;y<H;y+=4){
    const d=focal*.5/(y-horizon),fade=clamp(1-d/55,.22,1);
    for(let x=0;x<W;x+=4){
      const lateral=(x-W/2)/focal,wx=p.x+d*(sx+px*lateral),wz=p.z+d*(cz+pz*lateral);
      const cell=cellAt(world,wx,wz),grid=(wx%1+1)%1<.035||(wz%1+1)%1<.035;
      const noise=((Math.floor(wx*5)*19+Math.floor(wz*5)*31)&7)*2;
      const rgb=cell===5?[23,31,32]:cell===3?[54+noise,83+noise,81+noise]:cell===4?[108,100,73]:[80+noise,88+noise,65+noise];
      ctx.fillStyle=tint(rgb,fade*(grid?.83:1));ctx.fillRect(x,y,4,4);
    }
  }
  for(let x=0;x<W;x++){
    const lateral=(x-W/2)/focal,ray=castRay(world,p.x,p.z,sx+px*lateral,cz+pz*lateral);
    depth[x]=ray.distance;
    const h=Math.min(H*8,focal/ray.distance),top=horizon-h*.5;
    const shade=clamp(1-ray.distance/55,.23,1)*(ray.side?.78:1);
    const base=ray.cell===5?[22,31,32]:ray.cell===2?[116,87,54]:[148,151,129];
    ctx.fillStyle=tint(base,shade);ctx.fillRect(x,top,1,h);
    if(ray.cell!==5){
      if(ray.u<.018||ray.u>.98){ctx.fillStyle=tint(base,shade*.55);ctx.fillRect(x,top,1,h);}
      ctx.fillStyle=tint(base,shade*.67);ctx.fillRect(x,top+h*.33,1,Math.max(1,h*.009));ctx.fillRect(x,top+h*.67,1,Math.max(1,h*.009));
      ctx.fillStyle=tint(base,shade*1.12);ctx.fillRect(x,top,1,Math.max(1,h*.018));
      if(ray.cell===2){ctx.fillStyle=tint([44,39,29],shade);if(ray.u>.43&&ray.u<.57)ctx.fillRect(x,top+h*.15,1,h*.7);}
    }
  }
  // Sprites are sorted and clipped against walls, doors, and fog; concealed pawns never draw through them.
  const sprites=world.pawns.filter(q=>q.id!==p.id&&cellAt(world,q.x,q.z)!==5).map(q=>({q,z:(q.x-p.x)*sx+(q.z-p.z)*cz,x:(q.x-p.x)*px+(q.z-p.z)*pz})).filter(q=>q.z>.1).sort((a,b)=>b.z-a.z);
  for(const s of sprites){
    const size=focal/s.z,cx=W/2+s.x/s.z*focal,baseY=horizon+size*.5,top=baseY-size*.82;
    const color=s.q.hostile?[156,77,59]:[104,146,122],shade=clamp(1-s.z/55,.3,1);
    const drawPart=(rx,ry,rw,rh,rgb)=>{const left=Math.floor(cx+rx*size),right=Math.ceil(cx+(rx+rw)*size);ctx.fillStyle=tint(rgb,shade);for(let x=Math.max(0,left);x<Math.min(W,right);x++)if(s.z<depth[x])ctx.fillRect(x,top+ry*size,1,Math.max(1,rh*size));};
    if(s.q.downed){drawPart(-.3,.69,.6,.11,color);drawPart(-.37,.67,.15,.13,[191,158,116]);continue;}
    drawPart(-.21,.75,.42,.04,[39,47,37]);drawPart(-.13,.15,.26,.23,[202,170,129]);drawPart(-.14,.1,.28,.09,[69,62,45]);drawPart(-.19,.38,.38,.34,color);drawPart(-.23,.44,.08,.25,color);drawPart(.15,.44,.08,.25,color);drawPart(-.13,.71,.10,.1,[52,58,48]);drawPart(.03,.71,.10,.1,[52,58,48]);drawPart(.10,.50,.19,.065,[43,48,43]);
    // Tiny diegetic relation marker; no labels or extra information from unseen pawns.
    drawPart(-.035,.015,.07,.025,s.q.hostile?[227,121,88]:[182,218,141]);
  }
  // Original geometric held-weapon silhouette is decorative; live ammunition and shots are game-owned.
  if(engaged() && world.weapon?.label){
    const bob=active()&&keys.size?Math.sin(now*.009)*2:0;
    ctx.save();ctx.translate(W*.65,H+bob);ctx.fillStyle='#55594a';ctx.beginPath();ctx.moveTo(-50,0);ctx.lineTo(-28,-49);ctx.lineTo(0,-56);ctx.lineTo(23,0);ctx.fill();ctx.fillStyle='#262e2a';ctx.beginPath();ctx.moveTo(-12,0);ctx.lineTo(-5,-83);ctx.lineTo(8,-110);ctx.lineTo(21,-106);ctx.lineTo(32,-71);ctx.lineTo(52,0);ctx.fill();ctx.fillStyle='#6d7560';ctx.fillRect(0,-76,18,42);ctx.fillStyle='#141c19';ctx.fillRect(9,-113,9,23);ctx.fillStyle='#99ad83';ctx.fillRect(12,-112,2,3);if(demo&&now<muzzle){ctx.fillStyle='#ffe2a3';ctx.beginPath();ctx.moveTo(4,-112);ctx.lineTo(0,-130);ctx.lineTo(12,-123);ctx.lineTo(19,-143);ctx.lineTo(22,-122);ctx.lineTo(33,-127);ctx.lineTo(24,-110);ctx.fill();}ctx.restore();
  }
}
function hud() {
  const p=self(),w=world?.weapon;
  $('mode').textContent=demo?'OFFLINE DEMO':world?.combatMode||'LIVE BRIDGE';
  $('connection').textContent=connected?(world?.paused?'Ⅱ PAUSED':'● CONNECTED'):'○ RECONNECTING';
  $('pawn-name').textContent=p?.name||'Awaiting colonist';
  $('coordinates').textContent=p?`SECTOR ${world.mapId} / ${Math.floor(p.x)} : ${Math.floor(p.z)} / ${String(Math.round(yaw*180/Math.PI)).padStart(3,'0')}°`:'RIMWORLD / LOCAL BRIDGE';
  $('health').textContent=p?`${Math.round(clamp(p.health,0,1)*100)}%`:'—';
  $('health-bar').style.width=p?`${clamp(p.health,0,1)*100}%`:'0';
  $('weapon').textContent=w?.label||'UNARMED';$('ammo').textContent=w?.ammo>=0?w.ammo:'—';$('capacity').textContent=`/ ${w?.capacity>=0?w.capacity:'—'}`;$('weapon-status').textContent=w?.status||'No weapon data';
  $('simulation').textContent=demo?'OFFLINE TRAINING · SIMULATED COMBAT':'RIMWORLD AUTHORITATIVE';
  $('notice').textContent=world?.paused?'RIMWORLD IS PAUSED':!connected?connectionMessage:world?.message||'';
  $('overlay').classList.toggle('hidden',engaged() && ready());
  $('enter').disabled=!ready();
  $('keyboard-enter').disabled=!ready();
  $('capture-error').textContent=captureMessage;
  $('help').textContent=keyboardMode?'WASD MOVE · ← → LOOK · LMB FIRE · R RELOAD · ESC RELEASE':'WASD MOVE · MOUSE LOOK · LMB FIRE · R RELOAD · ESC RELEASE';
  $('enter').firstChild.textContent=ready()?'Enter perspective ':'Waiting for colonist ';
  $('overlay-title').textContent=demo?'The training grounds.':world?.paused?'Time stands still.':p?.downed?'Colonist downed.':!connected?'Establishing field link.':'Step into the Rim.';
  $('overlay-body').textContent=demo?'Offline demo with simulated movement and target damage. No game is connected. Explore the colony, aim at terracotta targets, and test the controls.':!connected?(token?connectionMessage:'Open this client using the link in RimWorld to include its local access token. You can explore the offline demo while the game is unavailable.'):world?.paused?'Unpause RimWorld, then start a new field link from your colonist to resume control.':!p||!world?.active?(connectionMessage||'Select a colonist and start a new field link in RimWorld.'):p.downed?'Your colonist is downed. Select another able colonist from RimWorld.':'Movement and combat run in RimWorld. Capture the mouse or use arrow-key look; press Escape any time to release control.';
}
let lastHud=0;
function frame(now) {
  const dt=Math.min(.05,(now-lastFrame)/1000);lastFrame=now;
  if(!demo && now>=nextPoll){nextPoll=now+(connected?100:1000);void poll();}
  if(!demo && connected && lastState && now-lastState>2200){connected=false;disengage();}
  if(active() && keyboardMode) yaw=((yaw+(Number(keys.has('ArrowRight'))-Number(keys.has('ArrowLeft')))*dt*1.8)%TAU+TAU)%TAU;
  if(now-lastInput>=50){lastInput=now;void sendInput();}
  if(demo)updateDemo(dt,now);
  render(now);
  if(now-lastHud>100){lastHud=now;hud();}
  requestAnimationFrame(frame);
}
hud();requestAnimationFrame(frame);
