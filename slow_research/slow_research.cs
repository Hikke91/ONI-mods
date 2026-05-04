using System;
using System.Reflection;
using Database;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using Klei.AI;

namespace slow_research
{
	public class Patches
	{
		[HarmonyPatch(typeof(ResearchCenter))]
		[HarmonyPatch("OnWorkTick")]
		public class ResearchCenter_OnWorkTick_Patch
		{
			public static void Postfix(ResearchCenter __instance, WorkerBase worker, ElementConverter ___elementConverter)
			{
				float speed = 0f + __instance.GetEfficiencyMultiplier(worker); //in original code, this is for some reason 2f+multiplier
																			   //speed *= 0.7f; //lets reduce the research speed by a further 30% while we are at it
				if (Game.Instance.FastWorkersModeActive)
					speed *= 2f;
				___elementConverter.SetWorkSpeedMultiplier(speed);
			}
		}

		[HarmonyPatch(typeof(Database.AttributeConverters))]
		[HarmonyPatch(MethodType.Constructor)]
		public class AttributeConverters_Constructor_Patch
		{
			public static void Postfix(Database.AttributeConverters __instance)
			{
				__instance.ResearchSpeed.multiplier = 0.2f; //reduces research attribute bonus from +40% to +20% r.speed per point
			}
		}

		[HarmonyPatch(typeof(Db))]
		[HarmonyPatch("Initialize")]
		public class Db_Initialize_Patch
		{
			public static void Postfix()
			{
				Db db = Db.Get();
				db.Techs.resources.ForEach(delegate (Tech tech)
				{
					foreach (string key in tech.costsByResearchTypeID.Keys.ToList())
					{
						tech.costsByResearchTypeID[key] *= 2; //doubles the research point cost of all techs
					}
				});
				
				db.DuplicantStatusItems.LaboratoryWorkEfficiencyBonus.resolveStringCallback = (Func<string, object, string>)((str, data) =>
				{
					string str3 = string.Format((string)STRINGS.DUPLICANTS.STATUSITEMS.LABORATORYWORKEFFICIENCYBONUS.NO_BUILDING_WORK_ATTRIBUTE, (object)GameUtil.AddPositiveSign(GameUtil.GetFormattedPercent(40f), true)); //this only changes the text, not the actual speed
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
				if ((bool)Traverse.Create(__instance).Field("useLaboratoryEfficiencyBonus").GetValue() && (bool)Traverse.Create(__instance).Field("currentlyInLaboratory").GetValue())
					a += 0.4f; //this is the only line that we change, changing lab bonus from +10% to +40%
				__result = Mathf.Max(a, (float)Traverse.Create(__instance).Field("minimumAttributeMultiplier").GetValue());
			}
		}

	}
}