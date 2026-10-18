using RimFront;
int checks = 0;
void Facing(float pawn, float viewer, int expected, string scenario)
{
    checks++;
    int actual = PawnSpriteView.Facing(pawn, viewer);
    if(actual != expected) throw new Exception($"{scenario}: expected {expected}, got {actual}");
}
// Rotate the entire encounter: front/back/left/right must remain viewer-relative.
foreach(int yaw in new[]{0,90,180,270,360,-360,720})
{
    Facing(yaw,yaw,2,"pawn looking at viewer shows front");
    Facing(yaw,yaw+180,0,"pawn looking away shows back");
    Facing(yaw,yaw+90,1,"viewer on pawn right sees right-facing art");
    Facing(yaw,yaw-90,3,"viewer on pawn left sees left-facing art");
}
Facing(0,44.99f,2,"front before octant boundary");
Facing(0,45.01f,1,"side after octant boundary");
Facing(359,1,2,"wrap across north");
Facing(1,359,2,"wrap across north reversed");
Facing(float.NaN,0,2,"invalid facing falls back to front");
Facing(0,float.PositiveInfinity,2,"invalid observer falls back to front");
Console.WriteLine($"PASS: {checks} viewpoint-based pawn sprite direction checks. No GPU or game session started.");
