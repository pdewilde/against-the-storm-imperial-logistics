using Eremite;
using Eremite.Model;
using Eremite.Services;
using Eremite.Services.World;

namespace ATS_ImperialLogistics.Helpers
{
    /// <summary>
    /// Inherits from Eremite.Services.Serviceable so all protected static game/meta/world service properties
    /// are cleanly and type-safely accessible across the plugin without reflection.
    /// </summary>
    internal sealed class Svc : Serviceable
    {
        public static Settings Cfg => Settings;
        public static bool Active => IsGameActive && GameController != null;
        public static IStateService State => StateService;
        public static IStorageService Storage => StorageService;
        public static IBuildingsService Buildings => BuildingsService;
        public static IWorkshopsService Workshops => WorkshopsService;
        public static IMapService Map => MapService;
        public static IConstructionService Construction => ConstructionService;
        public static IResourcesService Resources => ResourcesService;
        public static IDepositsService Deposits => DepositsService;
        public static IActorsService Actors => ActorsService;
        public static IOrdersService Orders => OrdersService;
        public static IRecipesService Recipes => RecipesService;
        public static IReputationService Reputation => ReputationService;
        public static IResolveService Resolve => ResolveService;
        public static IRacesService Races => RacesService;
        public static IVillagersService Villagers => VillagersService;
        public static IEffectsService Effects => EffectsService;
        public static IHostilityService Hostility => HostilityService;
        public static IAutomatonsService Automatons => AutomatonsService;
        public static INewsService News => NewsService;
        public static IModeService Mode => ModeService;
        public static IGameBlackboardService Blackboard => GameBlackboardService;
        public static IGameTimeService GameTimeSvc => GameTimeService;
        public static IGameContentService GameContent => GameContentService;
        public static IRoadsService Roads => RoadsService;
        public static IBiomeService BiomeSvc => BiomeService;

        // Meta & World Services
        public static ITextsService Texts => TextsService;
        public static IProfilesService Profiles => ProfilesService;
        public static IMetaEconomyService MetaEconomy => MetaEconomyService;
        public static IMetaPerksService MetaPerks => MetaPerksService;
        public static IMetaStateService MetaState => MetaStateService;
        public static IIronmanService Ironman => IronmanService;
        public static IWorldStateService WorldState => WorldStateService;
        public static ICycleEffectsService CycleEffects => CycleEffectsService;

        public static void PlayClickSuccess()
        {
            SoundsManager?.PlayButtonSound();
        }

        public static void PlayClickFail()
        {
            SoundsManager?.PlayFailedSound();
        }
    }
}
