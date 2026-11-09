using System;
using System.Reflection;
using Database;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using Klei.AI;
using KMod;
using PeterHan.PLib.Core;
using PeterHan.PLib.PatchManager;
using PeterHan.PLib.Options;
using Newtonsoft.Json;

namespace slow_research
{
	public sealed class SlowResearch : UserMod2
	{
		internal static Mod ThisMod {get; private set;}
		public static SlowResearchOptions Options;

		public override void OnLoad(Harmony harmony)
		{
			base.OnLoad(harmony);
			PUtil.InitLibrary();
			new PPatchManager(harmony).RegisterPatchClass(typeof(SlowResearch));
			new POptions().RegisterOptions(this, typeof(SlowResearchOptions));
			SlowResearch.Options = POptions.ReadSettings<SlowResearchOptions>() ?? new SlowResearchOptions();
		}

	}

	[JsonObject]
	public class SlowResearchOptions
	{
		[Option("Cost multiplier", "Multiplies the cost of all research by this much")]
		[Limit(1,10)]
		public float CostMultiplier {get; set;}
		[Option("Lab bonus","Lab room bonus to research speed in percentage (vanilla value is 10%)")]
		[Limit(0f,100f)]
		public int LabModifier {get; set;}
		[Option("Research speed multiplier","Additional multiplier to general research speed")]
		[Limit(0.001,1)]
		public float ResearchSpeedMultiplier {get; set;}
		[Option("Attribute bonus","Science attribute bonus to research speed per point in percentage (vanilla is 40%)")]
		[Limit(0f,100f)]
		public int AttributeBonus {get; set;}

		[Option("Novice Research multiplier","Additional research speed multiplier for novice research","Additional Multipliers")]
		[Limit(0.001,1)]
		public float NoviceResearchMultiplier {get; set;}
		[Option("Advanced Research multiplier","Additional research speed multiplier for advanced research","Additional Multipliers")]
		[Limit(0.001,1)]
		public float AdvancedResearchMultiplier {get; set;}
		[Option("Materials Science Research multiplier","Additional research speed multiplier for materials research","Additional Multipliers")]
		[Limit(0.001,1)]
		public float MaterialResearchMultiplier {get; set;}
		[Option("Data Analysis Research multiplier","Additional research speed multiplier for data analysis research","Additional Multipliers")]
		[Limit(0.001,1)]
		public float DataResearchMultiplier {get; set;}

		public SlowResearchOptions(){
			CostMultiplier=2f;
			ResearchSpeedMultiplier=1f;
			AttributeBonus=20;
			LabModifier=40;
			NoviceResearchMultiplier=1f;
			AdvancedResearchMultiplier=1f;
			MaterialResearchMultiplier=1f;
			DataResearchMultiplier=1f;
		}
	}

	public class Patches
	{
		[HarmonyPatch(typeof(ResearchCenter))]
		[HarmonyPatch("OnWorkTick")]
		public class ResearchCenter_OnWorkTick_Patch
		{
			public static void Postfix(ResearchCenter __instance, WorkerBase worker, ElementConverter ___elementConverter)
			{
				float speed = 0f + __instance.GetEfficiencyMultiplier(worker); //in original code, this is for some reason 2f+multiplier
				float multiplier = SlowResearch.Options.ResearchSpeedMultiplier;
				float noviceMultiplier = SlowResearch.Options.NoviceResearchMultiplier;
				float advancedMultiplier = SlowResearch.Options.AdvancedResearchMultiplier;
				float dataMultiplier = SlowResearch.Options.DataResearchMultiplier;
				speed *= multiplier; //additional multiplier on research speed, by default 1
				if (__instance.research_point_type_id.Equals("basic"))
					speed *= noviceMultiplier;
				if (__instance.research_point_type_id.Equals("advanced"))
					speed *= advancedMultiplier;
				if (__instance.research_point_type_id.Equals("space")||__instance.research_point_type_id.Equals("orbital"))
					speed *= dataMultiplier;
				if (Game.Instance.FastWorkersModeActive)
					speed *= 2f;
				___elementConverter.SetWorkSpeedMultiplier(speed);
			}
		}

		[HarmonyPatch(typeof(NuclearResearchCenterConfig))]
		[HarmonyPatch("ConfigureBuildingTemplate")]
		public class NuclearResearchCenterConfig_ConfigureBuildingTemplate_Patch //Materials science does not inherit from ResearchCenter, so we have to adjust it seperately
		{
			public static void Postfix(GameObject go)
			{
				float multiplier = SlowResearch.Options.ResearchSpeedMultiplier*SlowResearch.Options.MaterialResearchMultiplier;
				NuclearResearchCenter nuclearResearchCenter = go.AddOrGet<NuclearResearchCenter>();
				nuclearResearchCenter.timePerPoint /= multiplier; //division, because our multiplier is for p/t, while this value is for t/p
			}
		}


		[HarmonyPatch(typeof(Database.AttributeConverters))]
		[HarmonyPatch(MethodType.Constructor)]
		public class AttributeConverters_Constructor_Patch
		{
			public static void Postfix(Database.AttributeConverters __instance)
			{
				float multiplier = 0.01f*SlowResearch.Options.AttributeBonus; //converting int percentage points to a float
				__instance.ResearchSpeed.multiplier = multiplier; //changes research attribute bonus to value specified in options
				//NOTE: changes made in mod settings do not take effect until game restarted
			}
		}

		[HarmonyPatch(typeof(Db))]
		[HarmonyPatch("Initialize")]
		public class Db_Initialize_Patch
		{
			public static void Postfix()
			{
				Db db = Db.Get();
				float multiplier = SlowResearch.Options.CostMultiplier;
				db.Techs.resources.ForEach(delegate (Tech tech)
				{
					foreach (string key in tech.costsByResearchTypeID.Keys.ToList())
					{
						tech.costsByResearchTypeID[key] *= multiplier; //multiplies the research point cost of all techs
						//NOTE: changes made in mod settings do not take effect until game restarted
					}
				});
				
				float labmodifier = (float)SlowResearch.Options.LabModifier;
				db.DuplicantStatusItems.LaboratoryWorkEfficiencyBonus.resolveStringCallback = (Func<string, object, string>)((str, data) =>
				{
					string str3 = string.Format((string)STRINGS.DUPLICANTS.STATUSITEMS.LABORATORYWORKEFFICIENCYBONUS.NO_BUILDING_WORK_ATTRIBUTE, (object)GameUtil.AddPositiveSign(GameUtil.GetFormattedPercent(labmodifier), true)); //this only changes the text, not the actual speed
					return string.Format(str, (object)str3);
				});
			}
		}
		
		[HarmonyPatch(typeof(Workable))]
		[HarmonyPatch("GetEfficiencyMultiplier")]
		public class Workable_Patch //this patch changes the lab efficiency bonus
		{
			public static void Postfix(WorkerBase worker, Workable __instance, float __result)
			{
				float a = 1f;
				if (Traverse.Create(__instance).Field("attributeConverter").GetValue() != null)
				{
					AttributeConverterInstance attributeConverter = worker.GetAttributeConverter((string)Traverse.Create(__instance).Field("attributeConverter").Field("Id").GetValue());
					if (attributeConverter != null)
						a += attributeConverter.Evaluate();
				}
				if ((bool)Traverse.Create(__instance).Field("lightEfficiencyBonus").GetValue())
				{
					int cell = Grid.PosToCell(worker.gameObject);
					if (Grid.IsValidCell(cell))
					{
						if (Grid.LightIntensity[cell] > TUNING.DUPLICANTSTATS.STANDARD.Light.NO_LIGHT)
						{
							__instance.currentlyLit = true;
							a += TUNING.DUPLICANTSTATS.STANDARD.Light.LIGHT_WORK_EFFICIENCY_BONUS;
							if ((Guid)Traverse.Create(__instance).Field("lightEfficiencyBonusStatusItemHandle").GetValue() == Guid.Empty)
								Traverse.Create(__instance).Field("lightEfficiencyBonusStatusItemHandle").SetValue(worker.OfferStatusItem(Db.Get().DuplicantStatusItems.LightWorkEfficiencyBonus, (object)__instance));
						}
						else
						{
							__instance.currentlyLit = false;
							if ((Guid)Traverse.Create(__instance).Field("lightEfficiencyBonusStatusItemHandle").GetValue() != Guid.Empty)
								worker.RevokeStatusItem((Guid)Traverse.Create(__instance).Field("lightEfficiencyBonusStatusItemHandle").GetValue());
						}
					}
				}
				float labmodifier = 0.01f*SlowResearch.Options.LabModifier; //converting int percentage points to a float
				if ((bool)Traverse.Create(__instance).Field("useLaboratoryEfficiencyBonus").GetValue() && (bool)Traverse.Create(__instance).Field("currentlyInLaboratory").GetValue())
					a += labmodifier; //this is the only line that we change, changing lab bonus from +10% to value defined in mod settings
				__result = Mathf.Max(a, (float)Traverse.Create(__instance).Field("minimumAttributeMultiplier").GetValue());
			}
		}

	}
}