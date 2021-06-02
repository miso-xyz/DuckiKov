using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace DuckiKovDotNET
{
    class Program
    {
        static string path;
        static ModuleDefMD asm;

        static void Main(string[] args)
        {
            Console.Title = "DuckiKov";
            Console.WriteLine();
            Console.WriteLine(" DuckiKov v1.0 by misonothx");
            Console.WriteLine("  |- https://github.com/miso-xyz/DuckiKov");
            Console.WriteLine();
            try
            {
                asm = ModuleDefMD.Load(args[0]);
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(" Cannot load app!");
                goto end;
            }
            path = args[0];
            asm.Name = Path.GetFileName(args[0]);
            asm.EntryPoint.Name = "Main";
            asm.EntryPoint.DeclaringType.Name = "Entrypoint";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" Renamed Entrypoint & Module!");
            differienciateObjects();
            FixSizeOfs();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" Cleaning up Math...");
            fixMath();
            Console.WriteLine(" Cleaning up Control Flow...");
            cleanCflow();
            fixMath();
            removeUselessIfs();
            Console.WriteLine(" Fixing up strings...");
            fixStrings();
            ModuleWriterOptions moduleWriterOptions = new ModuleWriterOptions(asm);
            moduleWriterOptions.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
            moduleWriterOptions.Logger = DummyLogger.NoThrowInstance;
            NativeModuleWriterOptions nativeModuleWriterOptions = new NativeModuleWriterOptions(asm, true);
            nativeModuleWriterOptions.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
            nativeModuleWriterOptions.Logger = DummyLogger.NoThrowInstance;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" Saving cleaned application...");
            if (asm.IsILOnly) { asm.Write(Path.GetFileNameWithoutExtension(args[0]) + "-DuckiKov" + Path.GetExtension(args[0]), moduleWriterOptions); } else { asm.NativeWrite(Path.GetFileNameWithoutExtension(args[0]) + "-DuckiKov" + Path.GetExtension(args[0])); }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine(" Successfully saved as '" + Path.GetFileNameWithoutExtension(args[0]) + "-DuckiKov" + Path.GetExtension(args[0]) + "'!");
            Console.WriteLine();
            end:
            Console.ResetColor();
            Console.Write(" Press any key to exit...");
            Console.ReadKey();
        }

        static void cleanCflow()
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        switch (inst.OpCode.Code)
                        {
                            case Code.Br_S:
                                if (methods.Body.Instructions[x + 1].OpCode.Equals(OpCodes.Ldstr))
                                {
                                    methods.Body.Instructions.RemoveAt(x);
                                    methods.Body.Instructions.RemoveAt(x);
                                    x -= 2;
                                }
                                break; 
                            case Code.Br:
                                if (((Instruction)inst.Operand).Offset == methods.Body.Instructions[x+1].Offset)
                                {
                                    methods.Body.Instructions.RemoveAt(x);
                                }
                                break;
                            
                        }
                    }
                }
            }
        }

        static void removeUselessIfs()
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        switch (inst.OpCode.Code)
                        {
                            case Code.Bne_Un:
                                try
                                {
                                    if (methods.Body.Instructions[x - 1].GetLdcI4Value() == methods.Body.Instructions[x - 2].GetLdcI4Value())
                                    {
                                        //methods.Body.Instructions.RemoveAt(methods.Body.Instructions.IndexOf((Instruction)inst.Operand));
                                        methods.Body.Instructions.RemoveAt(x - 2);
                                        methods.Body.Instructions.RemoveAt(x - 2);
                                        methods.Body.Instructions.RemoveAt(x - 2);
                                        x -= 3;
                                    }
                                }
                                catch { }
                                break;
                        }
                    }
                }
            }
        }

        static void fixMath()
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        switch (inst.OpCode.Code)
                        {
                            case Code.Add:
                            case Code.Sub:
                            case Code.Mul:
                            case Code.Div:
                            case Code.Xor:
                            case Code.Rem:
                                int calculated = 0;
                                try
                                {
                                    calculated = Calculate(new Instruction[] { methods.Body.Instructions[x - 1], methods.Body.Instructions[x - 2] }, inst.OpCode.Code);
                                    methods.Body.Instructions.RemoveAt(x - 2);
                                    methods.Body.Instructions.RemoveAt(x - 2);
                                    inst.OpCode = OpCodes.Ldc_I4;
                                    inst.Operand = calculated;
                                }
                                catch { }
                                break;
                        }
                    }
                }
            }
        }

        // taken from LostMyMind (https://github.com/miso-xyz/LostMyMind)
        static void FixSizeOfs()
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count(); x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        if (inst.OpCode.Equals(OpCodes.Sizeof) && inst.Operand != null)
                        {
                            switch (((TypeRef)inst.Operand).Name.ToLower())
                            {
                                case "boolean":
                                    inst.OpCode = OpCodes.Ldc_I4;
                                    inst.Operand = sizeof(Boolean);
                                    break;
                                case "single":
                                    //inst = OpCodes.Ldc_I4.ToInstruction(sizeof(Single));
                                    inst.OpCode = OpCodes.Ldc_I4;
                                    inst.Operand = sizeof(Single);
                                    break;
                                case "double":
                                    inst.OpCode = OpCodes.Ldc_I4;
                                    inst.Operand = sizeof(Double);
                                    break;
                                default:
                                    Console.WriteLine("unknown SizeOf! (" + ((TypeRef)inst.Operand).Name.ToLower() + ")");
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public static int Calculate(Instruction[] insts, Code calcType)
        {
            switch (calcType)
            {
                case Code.Add:
                    return insts[0].GetLdcI4Value() + insts[1].GetLdcI4Value();
                case Code.Sub:
                    return insts[0].GetLdcI4Value() - insts[1].GetLdcI4Value();
                case Code.Mul:
                    return insts[0].GetLdcI4Value() * insts[1].GetLdcI4Value();
                case Code.Div:
                    return insts[0].GetLdcI4Value() / insts[1].GetLdcI4Value();
                case Code.Xor:
                    return insts[0].GetLdcI4Value() ^ insts[1].GetLdcI4Value();
                case Code.Rem:
                    return insts[0].GetLdcI4Value() % insts[1].GetLdcI4Value();
                default:
                    return int.MinValue;
            }
        }

        static void fixStrings()
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        switch (inst.OpCode.Code)
                        {
                            case Code.Ldstr:
                                if (methods.Body.Instructions[x + 1].Operand.ToString().Contains("StringFixer"))
                                {
                                    inst.Operand = reverseString(inst.Operand.ToString());
                                    methods.Body.Instructions.RemoveAt(x + 1);
                                }
                                break;
                        }
                    }
                }
            }
        }

        public static string reverseString(string data)
        {
            char[] array = data.ToCharArray();
            Array.Reverse(array);
            return new string(array);
        }

        static void differienciateObjects()
        {
            int field_count_renamed = 0;
            foreach (TypeDef type in asm.Types)
            {
                foreach (FieldDef fields in type.Fields)
                {
                    fields.Name = "duck_" + field_count_renamed++ + "_" + fields.FieldType.TypeName;
                }
                foreach (MethodDef methods in type.Methods)
                {
                    if (type.IsGlobalModuleType)
                    {
                        foreach (Instruction inst in methods.Body.Instructions)
                        {
                            switch (inst.OpCode.Code)
                            {
                                case Code.Call:
                                    if (inst.Operand.ToString().Contains("System.Array::Reverse"))
                                    {
                                        methods.Name = "StringFixer";
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }
}
