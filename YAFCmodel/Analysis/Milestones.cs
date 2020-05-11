using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using YAFC.UI;

namespace YAFC.Model
{
    public class Milestones : Analysis
    {
        public static readonly Milestones Instance = new Milestones();
        
        public FactorioObject[] currentMilestones; 
        public Mapping<FactorioObject, ulong> milestoneResult;
        public ulong lockedMask { get; private set; }
        private Project project;

        public bool IsAccessibleWithCurrentMilesonts(FactorioObject obj) => (milestoneResult[obj] & lockedMask) == 1; 

        private void GetLockedMaskFromProject()
        {
            lockedMask = ~0ul;
            var index = 0;
            foreach (var milestone in currentMilestones)
            {
                index++;
                if (project.settings.Flags(milestone).HasFlags(ProjectPerItemFlags.MilestoneUnlocked))
                    lockedMask &= ~(1ul << index);
            }
        }

        private void ProjectSettingsChanged(bool visualOnly)
        {
            if (!visualOnly)
                GetLockedMaskFromProject();
        }

        public FactorioObject GetHighest(FactorioObject target, bool all)
        {
            if (target == null)
                return null;
            var ms = milestoneResult[target];
            if (!all)
                ms &= lockedMask;
            if (ms == 0)
                return null;
            var msb = MathUtils.HighestBitSet(ms)-1;
            return msb < 0 || msb >= currentMilestones.Length ? null : currentMilestones[msb];
        }
        
        [Flags]
        private enum ProcessingFlags : byte
        {
            InQueue = 1,
            Initial = 2
        }

        public override void Compute(Project project)
        {
            if (project.settings.milestones.Count == 0)
                project.settings.milestones.AddRange(Database.allSciencePacks);
            if (this.project == null)
            {
                this.project = project;
                project.settings.changed += ProjectSettingsChanged;
            }

            currentMilestones = project.settings.milestones.ToArray();
            GetLockedMaskFromProject();
            
            var time = Stopwatch.StartNew();
            var result = Database.objects.CreateMapping<ulong>();
            var processing = Database.objects.CreateMapping<ProcessingFlags>();
            var dependencyList = Dependencies.dependencyList;
            var reverseDependencies = Dependencies.reverseDependencies;
            var processingStack = new Stack<int>();

            for (var i = 0; i < currentMilestones.Length; i++)
            {
                var milestone = currentMilestones[i];
                result[milestone] = (1ul << (i + 1)) | 1;
            }

            foreach (var rootAccessbile in Database.rootAccessible)
            {
                result[rootAccessbile] = 1;
                processingStack.Push(rootAccessbile.id);
                processing[rootAccessbile] = ProcessingFlags.Initial | ProcessingFlags.InQueue;
            }

            var flagMask = 0ul;
            var opc = 0;
            for (var i = 0; i <= currentMilestones.Length; i++)
            {
                flagMask |= 1ul << i;
                if (i > 0)
                {
                    var milestone = currentMilestones[i-1];
                    Console.WriteLine("Processing milestone "+milestone.locName);
                    processingStack.Push(milestone.id);
                    processing[milestone] = ProcessingFlags.Initial | ProcessingFlags.InQueue;
                }

                while (processingStack.Count > 0)
                {
                    var elem = processingStack.Pop();
                    var entry = dependencyList[elem];

                    var cur = result[elem];
                    var eflags = cur;
                    var isInitial = (processing[elem] & ProcessingFlags.Initial) != 0;
                    processing[elem] = 0;

                    foreach (var list in entry)
                    {
                        if ((list.flags & DependencyList.Flags.RequireEverything) != 0)
                        {
                            foreach (var req in list.elements)
                            {
                                var reqFlags = result[req];
                                if (reqFlags == 0 && !isInitial)
                                    goto skip;
                                eflags |= result[req];
                            }
                        }
                        else
                        {
                            var groupFlags = 0ul;
                            foreach (var req in list.elements)
                            {
                                var acc = result[req];
                                if (acc == 0)
                                    continue;
                                if (acc < groupFlags || groupFlags == 0ul)
                                    groupFlags = acc;
                            }

                            if (groupFlags == 0 && !isInitial)
                                goto skip;
                            eflags |= groupFlags;
                        }
                    }
                    if (!isInitial)
                    {
                        if (eflags == cur || (eflags | flagMask) != flagMask)
                            continue;
                    }
                    else eflags &= flagMask;

                    //Console.WriteLine("Added object "+obj.locName+" ["+obj.GetType().Name+"] with mask "+eflags.ToString("X") + " (was "+cur.ToString("X")+")");
                    
                    result[elem] = eflags;
                    foreach (var revdep in reverseDependencies[elem])
                    {
                        if (processing[revdep] != 0 || result[revdep] != 0)
                            continue;
                        processing[revdep] = ProcessingFlags.InQueue;
                        processingStack.Push(revdep);
                    }
                    
                    skip:;

                    if (++opc > 1000000)
                        goto stop;
                }
            }
            
            stop:;
            Console.WriteLine("Milestones calculation finished after "+opc+" steps in "+time.ElapsedMilliseconds+" ms.");
            milestoneResult = result;
        }
    }
}