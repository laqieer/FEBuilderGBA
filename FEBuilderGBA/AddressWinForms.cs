using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FEBuilderGBA
{
    // WinForms-dependent Address methods kept here as a static helper class.
    // The core Address class (DataTypeEnum, constructor, classifier methods, clean static methods)
    // lives in FEBuilderGBA.Core/Address.cs.
    public static class AddressWinForms
    {
        static public void AddHeaderTSAPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ImageUtil.CalcByteLengthForHeaderTSAData(Program.ROM.Data, (int)U.toOffset(addr));
            }

            list.Add(new Address(addr, length, pointer, info , Address.DataTypeEnum.HEADERTSA));
        }

        static public void AddAddress(List<Address> list, InputFormRef InputFormRef, string info, uint[] pointerIndexes, Address.DataTypeEnum type = Address.DataTypeEnum.InputFormRef)
        {
            uint addr = InputFormRef.BaseAddress;
            uint length = InputFormRef.BlockSize * (InputFormRef.DataCount + 1);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            uint pointer = InputFormRef.BasePointer;
            if (!U.isSafetyOffset(pointer))
            {
                pointer = U.NOT_FOUND;
            }
            list.Add(new Address(addr, length, pointer, info, type, InputFormRef.BlockSize, pointerIndexes));
        }
        static public void AddAddressButDoNotLengthPuls1(List<Address> list, InputFormRef InputFormRef, string info, uint[] pointerIndexes, Address.DataTypeEnum type = Address.DataTypeEnum.InputFormRef_1)
        {
            uint addr = InputFormRef.BaseAddress;
            uint length = InputFormRef.BlockSize * (InputFormRef.DataCount);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            uint pointer = InputFormRef.BasePointer;
            if (!U.isSafetyOffset(pointer))
            {
                pointer = U.NOT_FOUND;
            }
            list.Add(new Address(addr, length, pointer, info, type, InputFormRef.BlockSize, pointerIndexes));
        }
        static public void AddAddressButIgnorePointer(List<Address> list, InputFormRef InputFormRef, string info, uint[] pointerIndexes)
        {
            uint addr = InputFormRef.BaseAddress;
            uint length = InputFormRef.BlockSize * (InputFormRef.DataCount + 1);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            uint pointer = U.NOT_FOUND;
            list.Add(new Address(addr, length, pointer, info, Address.DataTypeEnum.InputFormRef, InputFormRef.BlockSize, pointerIndexes));
        }

        static void AddAPAddress(List<Address> list, uint addr, uint pointer, string info, bool isPointerOnly)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            if (pointer != U.NOT_FOUND)
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ImageUtilAP.CalcAPLength(addr);
            }
            list.Add(new Address(addr, length, pointer, info, Address.DataTypeEnum.AP));
        }
        static public void AddAPPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            AddAPAddress(list, addr, pointer, info, isPointerOnly);
        }

        static void AddROMTCSAddress(List<Address> list, uint addr, uint pointer, string info, bool isPointerOnly)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            if (pointer != U.NOT_FOUND)
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ImageUtilAP.CalcROMTCSLength(addr);
            }
            list.Add(new Address(addr, length, pointer, info, Address.DataTypeEnum.ROMTCS));
        }
        static public void AddROMTCSPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            AddROMTCSAddress(list, addr, pointer, info, isPointerOnly);
        }

        public static void AddProcsAddress(List<Address> list, uint addr, uint pointer, string info, bool isPointerOnly)
        {
            addr = U.toOffset(addr);
            if (!U.isSafetyOffset(addr))
            {
                return;
            }
            if (pointer != U.NOT_FOUND)
            {
                pointer = U.toOffset(pointer);
                if (!U.isSafetyOffset(pointer))
                {
                    return;
                }
            }

            uint length;
            if (isPointerOnly)
            {
                length = 0;
            }
            else
            {
                length = ProcsScriptForm.CalcLengthAndCheck(addr);
                if (length == U.NOT_FOUND)
                {//procsではない.
                    return;
                }
            }
            list.Add(new Address(addr, length, pointer, info, Address.DataTypeEnum.PROCS));
        }
        static public void AddProcsPointer(List<Address> list, uint pointer, string info, bool isPointerOnly)
        {
            pointer = U.toOffset(pointer);
            if (!U.isSafetyOffset(pointer))
            {
                return;
            }
            uint addr = Program.ROM.u32(pointer);
            if (!U.isSafetyPointer(addr))
            {
                return;
            }
            AddProcsAddress(list, addr, pointer, info, isPointerOnly);
        }
    }
}
