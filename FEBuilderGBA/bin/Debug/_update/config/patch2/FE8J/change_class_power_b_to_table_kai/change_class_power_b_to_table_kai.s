.thumb

Class_Bonus_Start = (adr)

@org	0x0802C2F0

ldr	r0, [r0, #0]
push	{r7}
mov	r7, r0
ldr	r0, [r0, #0x28]
ldr	r1, [r3, #0x28]
orr	r0, r1
mov	r1, #0x80
lsl	r1, r1, #1
and	r0 ,r1

push	{r0}
mov	r0, #0

Loop:
lsl	r1, r0, #2	@*4
ldr	r2, Class_Bonus_Start
ldrb	r2, [r2, r1]
cmp	r2, #0
beq	Normal
ldrb	r1, [r3, #4]	@class id
cmp	r1, r2
beq	Unit
add	r0, #1
b	Loop

Unit:
lsl	r1, r0, #2	@*4
add	r1, #1
ldr	r2, Class_Bonus_Start
ldrb	r2, [r2, r1]
cmp	r2, #0
beq	GetValue
ldrb	r1, [r7, #4]	@unit id
cmp	r1, r2
beq	GetValue
add	r0, #1
b	Loop

GetValue:
lsl	r1, r0, #2	@*4
add	r1, #2
ldr 	r2, Class_Bonus_Start
ldrb	r2, [r2, r1]
add	r4, r2
pop	{r0}
b	end

Normal:
pop	{r0}
cmp	r0, #0x0	@‰º‹‰‚È‚ç•ªŠò
beq	end
add	r4, #60

end:
pop	{r7}
ldr	r1, =0x0802c318
mov	pc, r1

.ltorg
.align
adr: