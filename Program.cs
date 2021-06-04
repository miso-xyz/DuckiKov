using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
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
        static string enc_key;
        static string enc_IV;

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
            Console.WriteLine(" Cleaning up Locals...");
            fixLocals();
            Console.WriteLine(" Cleaning up Math...");
            fixMath(false);
            Console.WriteLine(" Cleaning up Control Flow...");
            cleanCflow();
            fixMath(true);
            removeUselessIfs();
            Console.WriteLine(" Fixing up strings...");
            getEncryptionKeys();
            fixStrings();
            Console.WriteLine(" Finnishing it up...");
            removeUselessIfs();
            fixMath(false);
            //removeUselessMathPattern();
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
                                if (((Instruction)inst.Operand).Offset == methods.Body.Instructions[x + 1].Offset)
                                {
                                    methods.Body.Instructions.RemoveAt(x);
                                }
                                if (x + 2 >= methods.Body.Instructions.Count) { continue; }
                                if (((Instruction)inst.Operand).Offset == methods.Body.Instructions[x + 2].Offset && methods.Body.Instructions[x + 1].OpCode.Equals(OpCodes.Ldstr))
                                {
                                    methods.Body.Instructions.RemoveAt(x);
                                    methods.Body.Instructions.RemoveAt(x);
                                    x -= 2;
                                }
                                break;
                            
                        }
                    }
                }
            }
        }

        // based on CursedLand's Local2Field Fixer (https://github.com/CursedLand/Local2Field-Fixer/)
        static void fixLocals()
        {
            Dictionary<string, TypeSig> fieldList = new Dictionary<string, TypeSig>();
            Dictionary<string, Local> fixedLocalList = new Dictionary<string, Local>();
            TypeDef CctorType = asm.GlobalType;

            foreach (TypeDef type in asm.Types)
            {
                foreach (FieldDef fields in type.Fields)
                {
                    if (!fields.IsStatic) { continue; }
                    fieldList.Add(fields.Name, fields.FieldSig.GetFieldType());
                }
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        switch (inst.OpCode.Code)
                        {
                            case Code.Ldsfld:
                            case Code.Stsfld:
                            case Code.Ldsflda:
                                if (inst.Operand is FieldDef)
                                {
                                    string fieldName = ((FieldDef)inst.Operand).Name;
                                    if (fieldList.ContainsKey(((FieldDef)inst.Operand).Name))
                                    {
                                        TypeSig temp_typeSig = null;
                                        fieldList.TryGetValue(fieldName, out temp_typeSig);
                                        Local fixedLocal = new Local(temp_typeSig, fieldName);
                                        methods.Body.Variables.Add(fixedLocal);
                                        CctorType.Fields.Remove((FieldDef)inst.Operand);
                                        inst.OpCode = CallField(inst.OpCode.Code);
                                        if (!fixedLocalList.ContainsKey(fieldName))
                                        {
                                            inst.Operand = fixedLocal;
                                            fixedLocalList.Add(fieldName, fixedLocal);
                                        }
                                        else
                                        {
                                            inst.Operand = fixedLocalList[fieldName];
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }
            }
        }

        static OpCode CallField(Code OpCode)
        {
            switch (OpCode)
            {
                case Code.Stsfld:
                    return OpCodes.Stloc;
                case Code.Ldsfld:
                    return OpCodes.Ldloc;
                case Code.Ldsflda:
                    return OpCodes.Ldloca;
            }
            return null;
        }

        static void removeUselessMathPattern() // unused, will be used if i can figure out a way to remove the "-4 + 4 + 1 - 1 + 1 - 1" pattern
        {
            foreach (TypeDef type in asm.Types)
            {
                foreach (MethodDef methods in type.Methods)
                {
                    for (int x = 0; x < methods.Body.Instructions.Count; x++)
                    {
                        Instruction inst = methods.Body.Instructions[x];
                        if (inst.OpCode.Equals(OpCodes.Ldc_I4))
                        {
                            if (methods.Body.Instructions[x + 1].Operand is Local && methods.Body.Instructions[x + 4].Operand is Local)
                            {
                                for (int x_ = 0; x_ < 17; x_++)
                                {
                                    methods.Body.Instructions.RemoveAt(x);
                                }
                            }
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

        static void fixMath(bool fixCalls = false)
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
                            case Code.Call:
                                if (fixCalls)
                                {
                                    if (inst.Operand is MemberRef)
                                    {
                                        switch (((MemberRef)inst.Operand).Name)
                                        {
                                            case "Sin":
                                                if (methods.Body.Instructions[x + 1].OpCode.Equals(OpCodes.Conv_I4))
                                                {
                                                    inst.OpCode = OpCodes.Ldc_I4;
                                                    inst.Operand = Convert.ToInt32(Math.Sin(Convert.ToDouble(methods.Body.Instructions[x - 1].Operand.ToString())));
                                                    methods.Body.Instructions.RemoveAt(x + 1);
                                                }
                                                else
                                                {
                                                    inst.OpCode = OpCodes.Ldc_R8;
                                                    inst.Operand = Math.Sin(Convert.ToDouble(methods.Body.Instructions[x - 1].Operand.ToString()));
                                                }
                                                methods.Body.Instructions.RemoveAt(x - 1);
                                                x--;
                                                break;
                                            case "Cos":
                                                if (methods.Body.Instructions[x + 1].OpCode.Equals(OpCodes.Conv_I4))
                                                {
                                                    inst.OpCode = OpCodes.Ldc_I4;
                                                    inst.Operand = Convert.ToInt32(Math.Cos(Convert.ToDouble(methods.Body.Instructions[x - 1].Operand.ToString())));
                                                    methods.Body.Instructions.RemoveAt(x + 1);
                                                }
                                                else
                                                {
                                                    inst.OpCode = OpCodes.Ldc_R8;
                                                    inst.Operand = Math.Cos(Convert.ToDouble(methods.Body.Instructions[x - 1].Operand.ToString()));
                                                }
                                                methods.Body.Instructions.RemoveAt(x - 1);
                                                x--;
                                                break;
                                        }
                                    }
                                }
                                break;
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
                                    x -= 2;
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
                                if (methods.Body.Instructions[x + 1].OpCode.Equals(OpCodes.Call))
                                {
                                    if (methods.Body.Instructions[x + 1].Operand.ToString().Contains("StringFixer"))
                                    {
                                        inst.Operand = reverseString(inst.Operand.ToString());
                                        methods.Body.Instructions.RemoveAt(x + 1);
                                    }
                                }
                                break;
                        }
                    }
                }
            }
        }

        public static string reverseString(string data)
        {
            string result;
            char[] array = data.ToCharArray();
            Array.Reverse(array);
            result = new string(array);
            if (enc_key != null && enc_IV != null)
            {
                byte[] array2;
                try
                {
                    array2 = Convert.FromBase64String(result);
                }
                catch { return data; }
                AesCryptoServiceProvider aesCryptoServiceProvider = new AesCryptoServiceProvider();
                aesCryptoServiceProvider.BlockSize = 0x80;
                aesCryptoServiceProvider.KeySize = 0x100;
                aesCryptoServiceProvider.Key = Encoding.ASCII.GetBytes("Ta284WGc29asWL2F");
                aesCryptoServiceProvider.IV = Encoding.ASCII.GetBytes("h6iAm3fHwFdVbuIH");
                aesCryptoServiceProvider.Padding = PaddingMode.PKCS7;
                aesCryptoServiceProvider.Mode = CipherMode.CBC;
                ICryptoTransform cryptoTransform = aesCryptoServiceProvider.CreateDecryptor(aesCryptoServiceProvider.Key, aesCryptoServiceProvider.IV);
                byte[] bytes = cryptoTransform.TransformFinalBlock(array2, 0, array2.Length);
                result = Encoding.ASCII.GetString(bytes);
                cryptoTransform.Dispose();
            }
            return result;
        }

        static void getEncryptionKeys()
        {
            MethodDef methods = asm.GlobalType.FindMethod("StringFixer");
            foreach (Instruction inst in methods.Body.Instructions)
            {
                if (inst.OpCode.Equals(OpCodes.Ldstr) && methods.Body.Instructions[methods.Body.Instructions.IndexOf(inst) + 2].OpCode.Equals(OpCodes.Callvirt))
                {
                    if (methods.Body.Instructions[methods.Body.Instructions.IndexOf(inst) + 2].Operand.ToString().Contains("System.Security.Cryptography.SymmetricAlgorithm::set_Key"))
                    {
                        enc_key = inst.Operand.ToString();
                    }
                    else if (methods.Body.Instructions[methods.Body.Instructions.IndexOf(inst) + 2].Operand.ToString().Contains("System.Security.Cryptography.SymmetricAlgorithm::set_IV"))
                    {
                        enc_IV = inst.Operand.ToString();
                    }
                }
            }
        }

        static void differienciateObjects()
        {
            //int field_count_renamed = 0;
            foreach (TypeDef type in asm.Types)
            {
                //foreach (FieldDef fields in type.Fields)
                //{
                //    fields.Name = "duck_" + field_count_renamed++ + "_" + fields.FieldType.TypeName;
                //}
                foreach (MethodDef methods in type.Methods)
                {
                    if (type.IsGlobalModuleType)
                    {
                        for (int x = 0; x < methods.Body.Instructions.Count; x++)
                        {
                            Instruction inst = methods.Body.Instructions[x];
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
