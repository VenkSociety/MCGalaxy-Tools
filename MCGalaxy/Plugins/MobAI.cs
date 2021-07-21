﻿using System;
using System.IO;
using System.Threading;

using MCGalaxy.Bots;
using MCGalaxy.Maths;

namespace MCGalaxy
{

    public sealed class RoamAI : Plugin
    {
        BotInstruction hostile;
        BotInstruction roam;

        public override string name { get { return "RoamAI"; } }
        public override string MCGalaxy_Version { get { return "1.9.3.1"; } }
        public override string creator { get { return "Venk"; } }

        public override void Load(bool startup)
        {
            hostile = new HostileInstruction();
            roam = new RoamInstruction();

            BotInstruction.Instructions.Add(hostile);
            BotInstruction.Instructions.Add(roam);
        }

        public override void Unload(bool shutdown)
        {
            BotInstruction.Instructions.Remove(hostile);
            BotInstruction.Instructions.Remove(roam);
        }
    }

    #region Hostile AI

    /* 
        Current AI behaviour:
        
        -   50% chance to stand still (moving when 0-2, still when 3-5)
        -   If not moving, wait for waitTime duration before executing next task
        -   Choose random coord within 8x8 block radius of player and try to go to it
        -   Do action for walkTime duration
        
     */

    sealed class HostileInstruction : BotInstruction
    {
        public HostileInstruction() { Name = "hostile"; }

        internal static Player ClosestPlayer(PlayerBot bot, int search)
        {
            int maxDist = search * 32;
            Player[] players = PlayerInfo.Online.Items;
            Player closest = null;

            foreach (Player p in players)
            {
                if (p.level != bot.level || p.invincible || p.hidden) continue;

                int dx = p.Pos.X - bot.Pos.X, dy = p.Pos.Y - bot.Pos.Y, dz = p.Pos.Z - bot.Pos.Z;
                int playerDist = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                if (playerDist >= maxDist) continue;

                closest = p;
                maxDist = playerDist;
            }
            return closest;
        }

        static bool MoveTowards(PlayerBot bot, Player p)
        {
            if (p == null) return false;

            int dx = p.Pos.X - bot.Pos.X, dy = p.Pos.Y - bot.Pos.Y, dz = p.Pos.Z - bot.Pos.Z;
            bot.TargetPos = p.Pos;
            bot.movement = true;

            Vec3F32 dir = new Vec3F32(dx, dy, dz);
            dir = Vec3F32.Normalise(dir);
            Orientation rot = bot.Rot;
            DirUtils.GetYawPitch(dir, out rot.RotY, out rot.HeadX);

            dx = Math.Abs(dx); dy = Math.Abs(dy); dz = Math.Abs(dz);

            // If we are very close to a player, switch from trying to look
            // at them to just facing the opposite direction to them
            if (dx < 4 && dz < 4)
            {
                rot.RotY = (byte)(p.Rot.RotY + 128);
            }
            bot.Rot = rot;

            return dx <= 8 && dy <= 16 && dz <= 8;
        }

        private readonly Random _random = new Random();

        public int RandomNumber(int min, int max)
        {
            return _random.Next(min, max);
        }

        public void DoStuff(PlayerBot bot, Metadata meta)
        {
            int stillChance = RandomNumber(0, 5); // Chance for the NPC to stand still
            int walkTime = RandomNumber(4, 8) * 5; // Time in milliseconds to execute a task
            int waitTime = RandomNumber(2, 5) * 5; // Time in milliseconds to wait before executing the next task

            int dx = RandomNumber(bot.Pos.X - (8 * 32), bot.Pos.X + (8 * 32)); // Random X location on the map within a 8x8 radius of the bot for the it to walk towards.
            int dz = RandomNumber(bot.Pos.Z - (8 * 32), bot.Pos.Z + (8 * 32)); // Random Z location on the map within a 8x8 radius of the bot for the it to walk towards.

            if (stillChance > 2)
            {
                meta.walkTime = walkTime;
            }

            else
            {
                Coords target;
                target.X = dx;
                target.Y = bot.Pos.Y;
                target.Z = dz;
                target.RotX = bot.Rot.RotX;
                target.RotY = bot.Rot.RotY;
                bot.TargetPos = new Position(target.X, target.Y, target.Z);

                bot.movement = true;

                if (bot.Pos.BlockX == bot.TargetPos.BlockX && bot.Pos.BlockZ == bot.TargetPos.BlockZ)
                {
                    bot.SetYawPitch(target.RotX, target.RotY);
                    bot.movement = false;
                }

                bot.AdvanceRotation();

                FaceTowards(bot);

                meta.walkTime = walkTime;
                bot.movement = false;
                meta.waitTime = waitTime;
            }
        }

        static void FaceTowards(PlayerBot bot)
        {
            int dstHeight = ModelInfo.CalcEyeHeight(bot);

            int dx = (bot.TargetPos.X) - bot.Pos.X, dy = bot.Rot.RotY, dz = (bot.TargetPos.Z) - bot.Pos.Z;
            Vec3F32 dir = new Vec3F32(dx, dy, dz);
            dir = Vec3F32.Normalise(dir);

            Orientation rot = bot.Rot;
            DirUtils.GetYawPitch(dir, out rot.RotY, out rot.HeadX);
            bot.Rot = rot;
        }

        public override bool Execute(PlayerBot bot, InstructionData data)
        {
            Metadata meta = (Metadata)data.Metadata;

            if (bot.Model == "skeleton") bot.movementSpeed = (int)Math.Round(3m * (short)98 / 100m);
            if (bot.Model == "zombie") bot.movementSpeed = (int)Math.Round(3m * (short)82 / 100m);

            if (bot.movementSpeed == 0) bot.movementSpeed = 1;

            int search = 12;
            //if (data.Metadata != null) search = (ushort)data.Metadata;
            Player closest = ClosestPlayer(bot, search);

            if (closest == null)
            {
                if (meta.walkTime > 0)
                {
                    meta.walkTime--;
                    bot.movement = true;
                    return true;
                }
                if (meta.waitTime > 0)
                {
                    meta.waitTime--;
                    return true;
                }

                DoStuff(bot, meta);

                bot.movement = false;
                bot.NextInstruction();
            }

            bool overlapsPlayer = MoveTowards(bot, closest);
            if (overlapsPlayer && closest != null) { bot.NextInstruction(); return false; }


            return true;
        }

        public override InstructionData Parse(string[] args)
        {
            InstructionData data =
                default(InstructionData);
            data.Metadata = new Metadata();
            return data;
        }

        public void Output(Player p, string[] args, StreamWriter w)
        {
            if (args.Length > 3)
            {
                w.WriteLine(Name + " " + ushort.Parse(args[3]));
            }
            else
            {
                w.WriteLine(Name);
            }
        }

        struct Coords
        {
            public int X, Y, Z;
            public byte RotX, RotY;
        }

        public override string[] Help { get { return help; } }
        static string[] help = new string[] {
            "%T/BotAI add [name] hostile",
            "%HCauses the bot behave as a hostile mob.",
        };
    }

    #endregion

    #region Roam AI

    /* 
        Current AI behaviour:
        
        -   50% chance to stand still (moving when 0-2, still when 3-5)
        -   If not moving, wait for waitTime duration before executing next task
        -   Choose random coord within 8x8 block radius of player and try to go to it
        -   Do action for walkTime duration
        
     */

    public sealed class Metadata { public int waitTime; public int walkTime; }

    sealed class RoamInstruction : BotInstruction
    {
        public RoamInstruction() { Name = "roam"; }

        private readonly Random _random = new Random();

        public int RandomNumber(int min, int max)
        {
            return _random.Next(min, max);
        }

        public void DoStuff(PlayerBot bot, Metadata meta)
        {
            int stillChance = RandomNumber(0, 5); // Chance for the NPC to stand still
            int walkTime = RandomNumber(4, 8) * 5; // Time in milliseconds to execute a task
            int waitTime = RandomNumber(2, 5) * 5; // Time in milliseconds to wait before executing the next task

            int dx = RandomNumber(bot.Pos.X - (8 * 32), bot.Pos.X + (8 * 32)); // Random X location on the map within a 8x8 radius of the bot for the it to walk towards.
            int dz = RandomNumber(bot.Pos.Z - (8 * 32), bot.Pos.Z + (8 * 32)); // Random Z location on the map within a 8x8 radius of the bot for the it to walk towards.

            if (stillChance > 2)
            {
                meta.walkTime = walkTime;
            }

            else
            {
                Coords target;
                target.X = dx;
                target.Y = bot.Pos.Y;
                target.Z = dz;
                target.RotX = bot.Rot.RotX;
                target.RotY = bot.Rot.RotY;
                bot.TargetPos = new Position(target.X, target.Y, target.Z);

                bot.movement = true;

                if (bot.Pos.BlockX == bot.TargetPos.BlockX && bot.Pos.BlockZ == bot.TargetPos.BlockZ)
                {
                    bot.SetYawPitch(target.RotX, target.RotY);
                    bot.movement = false;
                }

                bot.AdvanceRotation();

                FaceTowards(bot);

                meta.walkTime = walkTime;
                bot.movement = false;
                meta.waitTime = waitTime;
            }
        }

        static void FaceTowards(PlayerBot bot)
        {
            int dstHeight = ModelInfo.CalcEyeHeight(bot);

            int dx = (bot.TargetPos.X) - bot.Pos.X, dy = bot.Rot.RotY, dz = (bot.TargetPos.Z) - bot.Pos.Z;
            Vec3F32 dir = new Vec3F32(dx, dy, dz);
            dir = Vec3F32.Normalise(dir);

            Orientation rot = bot.Rot;
            DirUtils.GetYawPitch(dir, out rot.RotY, out rot.HeadX);
            bot.Rot = rot;
        }

        public override bool Execute(PlayerBot bot, InstructionData data)
        {
            Metadata meta = (Metadata)data.Metadata;

            if (meta.walkTime > 0)
            {
                meta.walkTime--;
                bot.movement = true;
                return true;
            }
            if (meta.waitTime > 0)
            {
                meta.waitTime--;
                return true;
            }

            DoStuff(bot, meta);

            bot.movement = false;
            bot.NextInstruction();
            return true;
        }

        public override InstructionData Parse(string[] args)
        {
            InstructionData data =
                default(InstructionData);
            data.Metadata = new Metadata();
            return data;
        }

        public void Output(Player p, string[] args, StreamWriter w)
        {
            if (args.Length > 3)
            {
                w.WriteLine(Name + " " + ushort.Parse(args[3]));
            }
            else
            {
                w.WriteLine(Name);
            }
        }

        struct Coords
        {
            public int X, Y, Z;
            public byte RotX, RotY;
        }

        public override string[] Help { get { return help; } }
        static string[] help = new string[] {
            "%T/BotAI add [name] roam",
            "%HCauses the bot behave freely.",
        };
    }

    #endregion
}