import test from 'node:test';
import assert from 'node:assert/strict';
import {cellAt,castRay,makeDemo,validSnapshot,InputBuffer} from './core.mjs';
const map = cells => ({width:3,height:3,cells,pawns:[],protocol:1});
test('DDA respects the first wall and closed door',()=>{
  const w=map([1,1,1,1,0,2,1,1,1]);
  assert.deepEqual(castRay(w,1.5,1.5,1,0),{distance:.5,cell:2,side:0,u:.5});
  assert.equal(castRay(w,1.5,1.5,0,1).distance,.5);
});
test('fog and out-of-map cells are opaque',()=>{
  const w=map([0,0,0,0,0,5,0,0,0]);
  assert.equal(castRay(w,1.5,1.5,1,0).cell,5);
  assert.equal(castRay(w,1.5,1.5,1,0).distance,.5);
  assert.equal(cellAt(w,-1,1),5);
  assert.equal(castRay(w,2.5,1.5,1,0).distance,.001);
});
test('snapshot validation rejects malformed geometry and non-finite pawn coordinates',()=>{
  assert.ok(validSnapshot(makeDemo()));
  assert.ok(!validSnapshot({...makeDemo(),cells:[]}));
  assert.ok(!validSnapshot({...makeDemo(),protocol:2}));
  assert.ok(!validSnapshot({...makeDemo(),pawns:[{x:NaN,z:1,health:1}]}));
});
test('demo fixture is deterministic and camera starts in open terrain',()=>{
  assert.deepEqual(makeDemo(),makeDemo());
  const w=makeDemo(),p=w.pawns[0];
  assert.equal(cellAt(w,p.x,p.z),0);
});
test('a click shorter than a network tick still sends exactly one fire request',()=>{
  const input=new InputBuffer();
  input.pressFire(); input.releaseFire();
  assert.equal(input.take(0).fire,true);
  assert.equal(input.take(0).fire,false);
});
test('held fire persists while reload is a one-shot request',()=>{
  const input=new InputBuffer();
  input.pressFire(); input.requestReload();
  assert.deepEqual(input.take(1),{forward:0,strafe:0,yaw:1,fire:true,reload:true});
  assert.deepEqual(input.take(1),{forward:0,strafe:0,yaw:1,fire:true,reload:false});
  input.releaseFire(); assert.equal(input.take(1).fire,false);
});
test('focus release cancels movement, held and queued fire, and reload',()=>{
  const input=new InputBuffer();
  input.keys.add('KeyW'); input.keys.add('KeyD'); input.pressFire(); input.requestReload();
  input.clear();
  assert.deepEqual(input.take(0),{forward:0,strafe:0,yaw:0,fire:false,reload:false});
  input.pressFire(); input.releaseFire(); input.clear();
  assert.equal(input.take(0).fire,false);
});
