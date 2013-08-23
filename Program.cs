using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace CHIP8 {
    class Program {
        static void Main(string[] args) {
            Chip8 cpu = new Chip8(new ConsoleScreen(), new ConsoleKeyboard());

            using (FileStream file = File.Open(args[0], FileMode.Open, FileAccess.Read)) {
                cpu.Load(file);
            }

            cpu.Run();
        }
    }

    interface IScreen {
        void Clear();
        void SetExtents(int width, int height);
        bool DrawSprite(int x, int y, int height, bool[,] sprite);
    }

    interface IKeyboard {
        void UpdateKeyboard();
        bool IsKeyDown(int key);
        int WaitForKey();
    }

    class ConsoleKeyboard : IKeyboard {
        private bool[] keys = new bool[16];

        public ConsoleKeyboard() {
        }

        private int GetConsoleKey(ConsoleKey key) {
            switch (key) {
                case ConsoleKey.NumPad0:
                    return 0x0;
                case ConsoleKey.NumPad1:
                    return 0x1;
                case ConsoleKey.NumPad2:
                    return 0x2;
                case ConsoleKey.NumPad3:
                    return 0x3;
                case ConsoleKey.NumPad4:
                    return 0x4;
                case ConsoleKey.NumPad5:
                    return 0x5;
                case ConsoleKey.NumPad6:
                    return 0x6;
                case ConsoleKey.NumPad7:
                    return 0x7;
                case ConsoleKey.NumPad8:
                    return 0x8;
                case ConsoleKey.NumPad9:
                    return 0x9;
                case ConsoleKey.A:
                    return 0xA;
                case ConsoleKey.B:
                    return 0xB;
                case ConsoleKey.C:
                    return 0xC;
                case ConsoleKey.D:
                    return 0xD;
                case ConsoleKey.E:
                    return 0xE;
                case ConsoleKey.F:
                    return 0xF;
            }
            return -1;
        }

        public bool IsKeyDown(int key) {
            return keys[key];
        }

        public int WaitForKey() {
            ConsoleKeyInfo key = Console.ReadKey(true);
            return GetConsoleKey(key.Key);
        }

        public void UpdateKeyboard() {
            while (Console.KeyAvailable) {
                int key = GetConsoleKey(Console.ReadKey(true).Key);
                if (key >= 0) {
                    keys[key] = !keys[key];
                }
            }
        }
    }

    class ConsoleScreen : IScreen {
        private bool[,] screen = new bool[32, 64];

        public ConsoleScreen() {
            Console.CursorVisible = false;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
        }

        public void Clear() {
            Console.Clear();
            screen = new bool[32, 64];
        }

        public void SetExtents(int width, int height) {
            screen = new bool[height, width];
            Console.SetWindowSize(width, height);
            Console.SetBufferSize(width, height);
        }

        public bool DrawSprite(int x, int y, int height, bool[,] sprite) {
            bool flipped = false;

            for (int row = 0; row < sprite.GetLength(1); row++) {
                for (int col = sprite.GetLength(0) - 1; col >= 0; col--) {
                    int xPos = (x + col) % screen.GetLength(1);
                    int yPos = (y + row) % screen.GetLength(0);
                    bool old = screen[yPos, xPos];

                    screen[yPos, xPos] ^= sprite[col, row];

                    if (!flipped) {
                        flipped = old && !screen[yPos, xPos];
                    }

                    if (old != screen[yPos, xPos]) {
                        Console.SetCursorPosition(xPos, yPos);
                        Console.Write(screen[yPos, xPos] ? '█' : ' ');
                    }
                }
            }

            return flipped;
        }
    }

    class Chip8 {
        public const int MemorySize = 3584;
        public const int NumRegisters = 16;
        private const int BaseAddress = 0x200;
        private const int OpSize = 2;

        private static readonly byte[][] Font = new byte[][] {
            new byte [] {0xF0, 0x90, 0x90, 0x90, 0xF0}, // 0
            new byte [] {0x20, 0x60, 0x20, 0x20, 0x70}, // 1
            new byte [] {0xF0, 0x10, 0xF0, 0x80, 0xF0}, // 2
            new byte [] {0xF0, 0x10, 0xF0, 0x10, 0xF0}, // 3
            new byte [] {0x90, 0x90, 0xF0, 0x10, 0x10}, // 4
            new byte [] {0xF0, 0x80, 0xF0, 0x10, 0xF0}, // 5
            new byte [] {0xF0, 0x80, 0xF0, 0x90, 0xF0}, // 6
            new byte [] {0xF0, 0x10, 0x20, 0x40, 0x40}, // 7
            new byte [] {0xF0, 0x90, 0xF0, 0x90, 0xF0}, // 8
            new byte [] {0xF0, 0x90, 0xF0, 0x10, 0xF0}, // 9
            new byte [] {0xF0, 0x90, 0xF0, 0x90, 0x90}, // A
            new byte [] {0xE0, 0x90, 0xE0, 0x90, 0xE0}, // B
            new byte [] {0xF0, 0x80, 0x80, 0x80, 0xF0}, // C
            new byte [] {0xE0, 0x90, 0x90, 0x90, 0xE0}, // D
            new byte [] {0xF0, 0x80, 0xF0, 0x80, 0xF0}, // E
            new byte [] {0xF0, 0x80, 0xF0, 0x80, 0x80}  // F
        };

        private Random rand = new Random();
        private Stack<int> stack = new Stack<int>(16);
        private byte[] memory = new byte[MemorySize];
        private byte[] registers = new byte[NumRegisters];
        private int registerI = 0;
        private int delayTimer = 0;
        private int soundTimer = 0;
        private int programCounter = BaseAddress;
        private int instructionSkip = 0;
        private IScreen screen;
        private IKeyboard keyboard;

        private Func<OpCode, bool>[] opProcessors;

        public Chip8(IScreen screen, IKeyboard keyboard) {
            this.screen = screen;
            this.keyboard = keyboard;

            screen.SetExtents(64, 32);

            opProcessors = new Func<OpCode, bool>[] {
                ClearOrReturn,
                JumpToAddress,
                Call,
                SkipIfEqual,
                SkipIfNotEqual,
                SkipIfEqualVY,
                SetToNN,
                AddNN,
                ArithmeticOps,
                SkipIfNotEqualVY,
                SetI,
                JumpToAddressOffset,
                SetToRandom,
                DrawSprite,
                SkipIfKey,
                ProcessMisc
            };
        }

        public void Load(Stream program) {
            ReadProgram(program);
        }

        public void Run() {
            int currentMs = 0;
            Stopwatch watch = new Stopwatch();
            watch.Start();

            while (true) {
                currentMs += (int) watch.ElapsedMilliseconds;
                while (currentMs >= 17) {
                    if (delayTimer > 0) {
                        delayTimer--;
                    }

                    if (soundTimer > 0) {
                        soundTimer--;
                    }

                    currentMs -= 17;
                    watch.Reset();
                    watch.Start();
                }

                keyboard.UpdateKeyboard();

                OpCode code = GetCurrentOpCode();

                if (instructionSkip > 0) {
                    Jump(programCounter + OpSize);
                    instructionSkip--;
                    continue;
                }

                if (code[0] > opProcessors.Length) {
                    throw new InvalidDataException("Op code " + code.ToString() + " not recognized!");
                }

                if (!opProcessors[code[0]](code)) {
                    Jump(programCounter + OpSize);
                }

                Thread.Sleep(1);
            }
        }

        private bool ClearOrReturn(OpCode code) {
            if (code.AsInteger() == 0x00E0) {
                screen.Clear();
            } else if (code.AsInteger() == 0x00EE) {
                Jump(stack.Pop());
            } else {
                throw new InvalidOperationException("Invalid opcode detected " + code + "!");
            }

            return false;
        }

        private bool JumpToAddress(OpCode code) {
            Jump(code.AsInteger() & 0xFFF);
            return true;
        }

        private bool Call(OpCode code) {
            int address = code.AsInteger() & 0xFFF;
            stack.Push(programCounter);
            Jump(address);
            return true;
        }

        private bool SkipIfEqual(OpCode code) {
            if (GetRegister(code[1]) == code.Second) {
                instructionSkip++;
            }
            return false;
        }

        private bool SkipIfNotEqual(OpCode code) {
            if (GetRegister(code[1]) != code.Second) {
                instructionSkip++;
            }
            return false;
        }

        private bool SkipIfEqualVY(OpCode code) {
            if (GetRegister(code[1]) == GetRegister(code[2])) {
                instructionSkip++;
            }
            return false;
        }

        private bool SetToNN(OpCode code) {
            SetRegister(code[1], code.Second);
            return false;
        }

        private bool AddNN(OpCode code) {
            int result = 0;
            AddBytes(GetRegister(code[1]), code.Second, out result);
            SetRegister(code[1], result);
            return false;
        }

        private bool ArithmeticOps(OpCode code) {
            int vx = code[1];
            int vy = code[2];
            
            switch (code[3]) {
                case 0:
                    SetRegister(vx, GetRegister(vy));
                    break;
                case 1:
                    SetRegister(vx, GetRegister(vx) | GetRegister(vy));
                    break;
                case 2:
                    SetRegister(vx, GetRegister(vx) & GetRegister(vy));
                    break;
                case 3:
                    SetRegister(vx, GetRegister(vx) ^ GetRegister(vy));
                    break;
                case 4: {
                        int result = 0;
                        int carry = AddBytes(GetRegister(vx), GetRegister(vy), out result);
                        SetRegister(vx, result);
                        SetRegister(0xF, carry);
                    }
                    break;
                case 5: {
                        byte notBorrow = 0;
                        if (GetRegister(vx) > GetRegister(vy)) {
                            notBorrow = 1;
                        }
                        SetRegister(vx, (GetRegister(vx) - GetRegister(vy)) % 256);
                        SetRegister(0xF, notBorrow);
                    }
                    break;
                case 6:
                    SetRegister(0xF, GetRegister(vx) & 1);
                    SetRegister(vx, GetRegister(vx) >> 1);
                    break;
                case 7: {
                        byte notBorrow = 0;
                        if (GetRegister(vy) > GetRegister(vx)) {
                            notBorrow = 1;
                        }
                        SetRegister(vx, (GetRegister(vy) - GetRegister(vx)) % 256);
                        SetRegister(0xF, notBorrow);
                    }
                    break;
                case 0xE:
                    SetRegister(0xF, (GetRegister(vx) & 0x80) >> 7);
                    SetRegister(vx, GetRegister(vx) << 1);
                    break;
                default:
                    throw new InvalidOperationException("Invalid opcode " + code.ToString());
            }
            return false;
        }

        private bool SkipIfNotEqualVY(OpCode code) {
            if (GetRegister(code[1]) != GetRegister(code[2])) {
                instructionSkip++;
            }
            return false;
        }

        private bool SetI(OpCode code) {
            registerI = code.AsInteger() & 0xFFF;
            return false;
        }

        private bool JumpToAddressOffset(OpCode code) {
            Jump(((code.AsInteger() & 0xFFF) + GetRegister(0)) % 0x1000);
            return true;
        }

        private bool SetToRandom(OpCode code) {
            byte[] buffer = new byte[1];
            rand.NextBytes(buffer);

            SetRegister(code[1], buffer[0] & code.Second);
            return false;
        }

        private bool DrawSprite(OpCode code) {
            int x = GetRegister(code[1]);
            int y = GetRegister(code[2]);
            int height = code[3];

            bool[,] sprite = new bool[8, height];
            for (int row = 0; row < height; row++) {
                int thisByte = 0;
                if (registerI >= 100 && registerI < BaseAddress) {
                    thisByte = (int)Font[registerI - 100][row];
                } else {
                    thisByte = GetMemory(registerI + row);
                }

                for (int bit = 7; bit >= 0; bit--) {
                    sprite[7 - bit, row] = ((thisByte >> bit) & 1) == 1;
                }
            }

            bool vf = screen.DrawSprite(x, y, height, sprite);
            SetRegister(0xF, vf ? 1 : 0);
            return false;
        }

        private bool SkipIfKey(OpCode code) {
            int key = GetRegister(code[1]);
            switch (code.Second) {
                case 0x9E:
                    if (keyboard.IsKeyDown(key)) {
                        instructionSkip++;
                    }
                    break;
                case 0xA1:
                    if (keyboard.IsKeyDown(key)) {
                        instructionSkip++;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Invalid op code " + code + "!");
            }
            return false;
        }

        private bool ProcessMisc(OpCode code) {
            int vx = GetRegister(code[1]);
            switch (code.Second) {
                case 0x07:
                    SetRegister(code[1], delayTimer);
                    break;
                case 0x0A:
                    int key = keyboard.WaitForKey();
                    if (key >= 0) {
                        SetRegister(code[1], key);
                    }
                    break;
                case 0x15:
                    delayTimer = vx;
                    break;
                case 0x18:
                    soundTimer = vx;
                    break;
                case 0x1E:
                    registerI += vx;
                    break;
                case 0x29:
                    registerI = 100 + vx;
                    break;
                case 0x33:{
                    int hundreds = vx / 100;
                    int tens = (vx - hundreds) / 10;
                    int ones = vx - hundreds - tens;
                    SetMemory(registerI, hundreds);
                    SetMemory(registerI, tens);
                    SetMemory(registerI, ones);
                    }
                    break;
                case 0x55:
                    for (int i = 0; i < code[1]; i++) {
                        SetMemory(registerI + i, GetRegister(i));
                    }
                    registerI += code[1];
                    break;
                case 0x65:
                    for (int i = 0; i < code[1]; i++) {
                        SetRegister(i, GetMemory(registerI + i));
                    }
                    registerI += code[1];
                    break;
                default:
                    throw new InvalidOperationException("Invalid opcode detected " + code + "!");
            }
            return false;
        }

        private int GetMemory(int address) {
            return (int) memory[address - BaseAddress];
        }

        private void SetMemory(int address, int value) {
            memory[address - BaseAddress] = (byte)value;
        }

        private int GetRegister(int register) {
            if (register < 0 || register > NumRegisters) {
                throw new InvalidOperationException("Attempt to access invalid register " + register + "!");
            }

            return registers[register];
        }

        private void SetRegister(int register, int value) {
            if (register < 0 || register > NumRegisters) {
                throw new InvalidOperationException("Attempt to access invalid register " + register + "!");
            }

            registers[register] = (byte) value;
        }


        private OpCode GetCurrentOpCode() {
            return new OpCode(
                memory[programCounter - BaseAddress], 
                memory[programCounter - BaseAddress + 1]);
        }

        private void Jump(int newAddress) {
            if (newAddress - BaseAddress < 0 || newAddress - BaseAddress >= MemorySize - 1) {
                throw new InvalidOperationException("Attempt to jump to invalid location (" + newAddress + ")");
            }

            programCounter = newAddress;
        }

        private void ReadProgram(Stream program) {
            for (int i = 0; i < MemorySize; i++) {
                int curVal = program.ReadByte();
                if (curVal < 0) {
                    break;
                }

                memory[i] = (byte) curVal;
            }
        }

        private byte AddBytes(int first, int second, out int result) {
            result = first + second;
            byte carry = (byte) ((result & 0xFF00) >> 8);
            result &= 0xFF;

            return (byte) (carry > 0 ? 1 : 0);
        }

        private class OpCode {
            public int First { get; private set; }
            public int Second { get; private set; }

            public OpCode(byte first, byte second) {
                First = (int) first;
                Second = (int) second;
            }

            public int AsInteger() {
                return First << 8 | Second;
            }

            public int this[int nibble] {
                get {

                    switch (nibble) {
                        case 0:
                            return (First & 0xF0) >> 4;
                        case 1:
                            return First & 0xF;
                        case 2:
                            return (Second & 0xF0) >> 4;
                        case 3:
                            return Second & 0xF;
                    }

                    return -1;
                }
            }

            public override string ToString() {
                return AsInteger().ToString("x4");
            }
        }
    }
}
