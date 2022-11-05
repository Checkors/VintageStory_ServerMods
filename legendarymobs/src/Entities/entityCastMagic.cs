﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace legendarymobs.src
{
    public class EntityCastMagic : Entity
    {
        protected bool beforeCollided;
        protected bool stuck;

        protected long msLaunch;
        protected Vec3d motionBeforeCollide = new Vec3d();

        protected CollisionTester collTester = new CollisionTester();

        public Entity FiredBy;
        public float Damage;
        public ItemStack ProjectileStack;

        public bool NonCollectible;
        public float collidedAccum;

        public override bool IsInteractable
        {
            get { return false; }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            msLaunch = World.ElapsedMilliseconds;
            if (ProjectileStack != null)
            {
                if (ProjectileStack.Collectible != null)
                {
                    ProjectileStack.ResolveBlockOrItem(World);
                }
            }
            

            GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (ShouldDespawn) return;

            EntityPos pos = SidedPos;

            stuck = Collided;
            if (stuck)
            {
                pos.Pitch = 0;
                pos.Roll = 0;
                pos.Yaw = GameMath.PIHALF;

                collidedAccum += dt;
                if (NonCollectible && collidedAccum > 1) Die();

            }
            else
            {
                pos.Pitch = (World.ElapsedMilliseconds / 300f) % GameMath.TWOPI;
                pos.Roll = 0;
                pos.Yaw = (World.ElapsedMilliseconds / 400f) % GameMath.TWOPI;
            }

            if (stuck)
            {
                if (!beforeCollided && World is IServerWorldAccessor)
                {
                    float strength = GameMath.Clamp((float)motionBeforeCollide.Length() * 4, 0, 1);

                    if (CollidedHorizontally)
                    {
                        pos.Motion.X = motionBeforeCollide.X * 0.8f;
                        pos.Motion.Z = motionBeforeCollide.Z * 0.8f;

                        if (strength > 0.08f && World.Rand.NextDouble() > 0.2f)
                        {
                            World.SpawnCubeParticles(SidedPos.XYZ.OffsetCopy(0, 0.2, 0), ProjectileStack, 0.2f, 20);
                            Die();
                        }
                    }

                    if (CollidedVertically && motionBeforeCollide.Y <= 0)
                    {
                        pos.Motion.Y = GameMath.Clamp(motionBeforeCollide.Y * -0.3f, -0.1f, 0.1f);
                    }

                    World.PlaySoundAt(new AssetLocation("legendarymobs:sounds/strike"), this, null, false, 32, strength);

                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();
                }

                beforeCollided = true;
                return;
            }


            if (World is IServerWorldAccessor)
            {
                Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                    if (e.EntityId == this.EntityId || (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500) || !e.IsInteractable)
                    {
                        return false;
                    }

                    double dist = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z).ShortestDistanceFrom(ServerPos.X, ServerPos.Y, ServerPos.Z);
                    return dist < 0.5f;
                });

                if (entity != null)
                {
                    bool didDamage = entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Entity, SourceEntity = FiredBy == null ? this : FiredBy, Type = EnumDamageType.BluntAttack }, Damage);
                    World.PlaySoundAt(new AssetLocation("legendarymobs:sounds/strike"), this, null, false, 32);
                    World.SpawnCubeParticles(entity.SidedPos.XYZ.OffsetCopy(0, 0.2, 0), ProjectileStack, 0.2f, 20);

                    Die();
                    return;
                }
            }

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        public override bool CanCollect(Entity byEntity)
        {
            return !NonCollectible && Alive && World.ElapsedMilliseconds - msLaunch > 1000 && ServerPos.Motion.Length() < 0.01;
        }

        public override ItemStack OnCollected(Entity byEntity)
        {
            ProjectileStack.ResolveBlockOrItem(World);
            return ProjectileStack;
        }


        public override void OnCollideWithLiquid()
        {
            if (motionBeforeCollide.Y <= 0)
            {
                SidedPos.Motion.Y = GameMath.Clamp(motionBeforeCollide.Y * -0.5f, -0.1f, 0.1f);
                PositionBeforeFalling.Y = Pos.Y + 1;
            }

            base.OnCollideWithLiquid();
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            ProjectileStack.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();

            ProjectileStack = World == null ? new ItemStack(reader) : new ItemStack(reader, World);
        }
    }
}