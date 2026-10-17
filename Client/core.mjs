export const TAU = Math.PI * 2;
export const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
// Preserve short clicks between network ticks; clearing on focus loss cancels every pending action.
export class InputBuffer {
  constructor() { this.keys = new Set(); this.clear(); }
  clear() { this.keys.clear(); this.heldFire = false; this.pendingFire = false; this.pendingReload = false; }
  pressFire() { this.heldFire = true; this.pendingFire = true; }
  releaseFire() { this.heldFire = false; }
  requestReload() { this.pendingReload = true; }
  take(yaw) {
    const state = {forward:Number(this.keys.has('KeyW'))-Number(this.keys.has('KeyS')),strafe:Number(this.keys.has('KeyD'))-Number(this.keys.has('KeyA')),yaw,fire:this.heldFire||this.pendingFire,reload:this.pendingReload};
    this.pendingFire=false; this.pendingReload=false;
    return state;
  }
}
export function cellAt(world, x, z) {
  x = Math.floor(x); z = Math.floor(z);
  return x < 0 || z < 0 || x >= world.width || z >= world.height ? 5 : world.cells[z * world.width + x];
}
export function castRay(world, x, z, dx, dz, limit = 80) {
  let cx = Math.floor(x), cz = Math.floor(z);
  const sx = dx < 0 ? -1 : 1, sz = dz < 0 ? -1 : 1;
  const deltaX = Math.abs(1 / dx), deltaZ = Math.abs(1 / dz);
  let tx = (dx < 0 ? x - cx : cx + 1 - x) * deltaX;
  let tz = (dz < 0 ? z - cz : cz + 1 - z) * deltaZ;
  if (dx === 0) tx = Infinity;
  if (dz === 0) tz = Infinity;
  let side = 0, distance = 0, cell = cellAt(world, cx, cz);
  // Fog is opaque even when the camera is inside a fogged cell.
  if (cell === 5) return {distance: .001, cell, side, u: 0};
  for (let i = 0; i < 512; i++) {
    if (tx < tz) { distance = tx; tx += deltaX; cx += sx; side = 0; }
    else { distance = tz; tz += deltaZ; cz += sz; side = 1; }
    cell = cellAt(world, cx, cz);
    if (cell === 1 || cell === 2 || cell === 5 || distance >= limit) break;
  }
  const coordinate = side ? x + distance * dx : z + distance * dz;
  return { distance: Math.max(.001, distance), cell, side, u: coordinate - Math.floor(coordinate) };
}
export function validSnapshot(value) {
  return value && value.protocol === 1 && Number.isInteger(value.width) && Number.isInteger(value.height)
    && value.width > 0 && value.height > 0 && value.width <= 2048 && value.height <= 2048
    && Array.isArray(value.cells) && value.cells.length === value.width * value.height
    && value.cells.every(c => Number.isInteger(c) && c >= 0 && c <= 5)
    && Array.isArray(value.pawns) && value.pawns.every(p => Number.isFinite(p.x) && Number.isFinite(p.z) && Number.isFinite(p.health));
}
export function makeDemo() {
  const width = 32, height = 32, cells = new Array(width * height).fill(0);
  const put = (x,z,c) => {cells[z*width+x] = c;};
  for(let z=0;z<height;z++) for(let x=0;x<width;x++) {
    if(x===0||z===0||x===31||z===31) put(x,z,1);
    if(x>23&&z>22) put(x,z,5);
    if(x>3&&x<9&&z>20&&z<26) put(x,z,3);
  }
  for (const [x0,z0,w,h] of [[4,5,7,7],[19,4,8,9],[19,17,6,7],[3,15,7,4]]) {
    for(let z=z0;z<z0+h;z++) for(let x=x0;x<x0+w;x++) if(x===x0||z===z0||x===x0+w-1||z===z0+h-1) put(x,z,1);
    put(x0+Math.floor(w/2),z0+h-1,2);
  }
  for(const [x,z] of [[13,17],[14,17],[15,17],[17,10],[17,11],[11,23],[12,23]]) put(x,z,4);
  return {protocol:1,gameVersion:'OFFLINE',status:'Training scene',combatMode:'DEMO',active:true,paused:false,tick:0,mapId:'training',width,height,cells,controlledPawnId:1,
    pawns:[{id:1,name:'Training colonist',x:14.5,z:7.5,health:1,hostile:false,downed:false},{id:2,name:'Target Alpha',x:14.5,z:21.5,health:1,hostile:true,downed:false},{id:3,name:'Target Bravo',x:17.5,z:25.5,health:1,hostile:true,downed:false},{id:4,name:'Colony scout',x:11.5,z:15.5,health:1,hostile:false,downed:false}],weapon:{label:'Training carbine',status:'Ready',ammo:24,capacity:24,canReload:true},message:'OFFLINE DEMO · No connection to RimWorld'};
}
