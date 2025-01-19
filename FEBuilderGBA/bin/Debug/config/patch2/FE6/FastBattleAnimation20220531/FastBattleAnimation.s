@Hook 080426F0 @FE6J
@Hook 0804B314 @FE7U
@Hook 08050C44 @FE8J
@Hook 0804FED0 @FE8U (InBattleMainRoutine; 0x0804FE40)
@r0 VCount

.thumb
.macro blh to, reg=r3
  ldr \reg, =\to
  mov lr, \reg
  .short 0xf800
.endm

LDR r3, =0x02023B20 @ FE6J (gKeyStObj)
@ LDR r3, =0x02024C78 @ FE7U (gKeysInstance)
@ LDR r3, =0x02024CC0 @ FE8J FE8U (KeyStatusBuffer@KeyStatusBuffer.FirstTickDelay )
LDRH r3, [r3, #0x4] @ KeyStatusBuffer@KeyStatusBuffer.Current
ldr r2, PressButton
AND r3 ,r2
beq WaitExit

@Aボタンが押されているので早くする!

@Dummy GeneralVBlankHandler
ldr r1, =0x03007FF8 @FE6J FE7U FE8J FE8U IRQCheckFlags
mov r0, #0x1
strh r0, [r1, #0x0]   @IRQCheckFlags
blh 0x08000F20   @FE6J IncGameTime
@blh 0x08000F48   @FE7U IncGameTime
@blh 0x08000cf0   @FE8J IncrementGlobalClock
@blh 0x08000d40   @FE8U IncrementGlobalClock

ldr r0, =0x020258cc @FE6J gProcTreeRootArray
@ldr r0, =0x02026a30 @FE7U gProcTreeRoots
@ldr r0, =0x02026A70 @FE8J FE8U 6CPointer@MainArray[0].pointer
ldr r0, [r0, #0x0]
blh 0x08003E44   @FE6J Proc_Run
@blh 0x08004690   @FE7U Proc_Run
@blh 0x08002dd4   @FE8J Exec6C
@blh 0x08002e84   @FE8U Exec6C

blh 0x08002dc4   @FE6J SyncLoOam
@blh 0x08003338   @FE7U SyncLoOam
@blh 0x080020cc   @FE8J FlushPrimaryOAM
@blh 0x0800217c   @FE8U FlushPrimaryOAM

ldr r1, =0x0202aa08 @FE6J gBmSt
@ldr r1, =0x0202bbb8 @FE7U gBmStatus
@ldr r1, =0x0202BCAC @FE8J BattleMapState@gGameState.boolMainLoopEnded
@ldr r1, =0x0202BCB0 @FE8U BattleMapState@gGameState.boolMainLoopEnded
ldrb r0, [r1, #0x0] @BattleMapState@gGameState.boolMainLoopEnded
cmp r0, #0x0
beq Exit
    mov r0, #0x0
    strb r0, [r1, #0x0]   @BattleMapState@gGameState.boolMainLoopEnded
    blh 0x080010CC   @FE7J SyncDispIo
    @blh 0x080011B0   @FE7U SyncDispIo
    @blh 0x08000e4c   @FE8J FlushLCDControl
    @blh 0x08000e9c   @FE8U FlushLCDControl
    blh 0x08001478   @FE6J SyncBgsAndPal
    @blh 0x080016EC   @FE7U SyncBgsAndPal
    @blh 0x080010fc   @FE8J FlushBackgrounds
    @blh 0x0800114c   @FE8U FlushBackgrounds
    blh 0x08002C04   @FE6J ApplyDataMoves
    @blh 0x08003178   @FE7U ApplyDataMoves
    @blh 0x08001fd8   @FE8J FlushTiles
    @blh 0x08002088   @FE8U FlushTiles
    blh 0x08002d6c   @FE6J SyncHiOam
    @blh 0x080032E0   @FE7U SyncHiOam
    @blh 0x08002088   @FE8J FlushSecondaryOAM
    @blh 0x08002138   @FE8U FlushSecondaryOAM
b   Exit

WaitExit:
@Aボタンが押されていないので、VBlankIntrWaitで待機させる
blh 0x0809dd30   @FE6J VBlankIntrWait
@blh 0x080BFA6C   @FE7U VBlankIntrWait
@blh 0x080d63d8   @FE8J VBlankIntrWait
@blh 0x080d16dc   @FE8U VBlankIntrWait

Exit:
pop {r0}
bx r0

.ltorg
PressButton:
