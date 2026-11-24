using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal static class GroupClassificationCatalog
{
    public const string OtherGenre = "otros";

    private static readonly (GroupGenre Genre, GroupSubgenre[] Subgenres)[] Entries =
    [
        Entry("accion", "Acción", ("hack-and-slash", "Hack and slash"), ("beat-em-up", "Beat 'em up"),
            ("run-and-gun", "Run and gun"), ("character-action", "Character action"), ("musou", "Musou"),
            ("sigilo", "Sigilo"), ("parkour", "Parkour")),
        Entry("aventura", "Aventura", ("point-and-click", "Point and click"), ("aventura-grafica", "Aventura gráfica"),
            ("aventura-textual", "Aventura textual"), ("aventura-exploracion", "Aventura de exploración")),
        Entry("rpg", "RPG", ("jrpg", "JRPG"), ("wrpg", "WRPG"), ("rpg-de-accion", "RPG de acción"),
            ("crpg", "CRPG"), ("tactical-rpg", "Tactical RPG"), ("dungeon-crawler", "Dungeon crawler"),
            ("soulslike", "Soulslike"), ("mmorpg", "MMORPG"), ("monster-collecting", "Monster collecting"),
            ("rpg-por-turnos", "RPG por turnos"), ("rpg-narrativo", "RPG narrativo")),
        Entry("disparos", "Disparos", ("fps", "FPS"), ("tps", "TPS"), ("hero-shooter", "Hero shooter"),
            ("tactical-shooter", "Tactical shooter"), ("arena-shooter", "Arena shooter"),
            ("looter-shooter", "Looter shooter"), ("extraction-shooter", "Extraction shooter"),
            ("bullet-hell", "Bullet hell"), ("boomer-shooter", "Boomer shooter")),
        Entry("plataformas", "Plataformas", ("metroidvania", "Metroidvania"),
            ("precision-platformer", "Precision platformer"), ("puzzle-platformer", "Puzzle platformer"),
            ("cinematic-platformer", "Cinematic platformer"), ("collectathon", "Collectathon"),
            ("endless-runner", "Endless runner")),
        Entry("estrategia", "Estrategia", ("rts", "RTS"), ("tbs", "TBS"), ("4x", "4X"),
            ("tower-defense", "Tower defense"), ("moba", "MOBA"), ("auto-battler", "Auto battler"),
            ("gran-estrategia", "Gran estrategia"), ("estrategia-tactica", "Estrategia táctica")),
        Entry("simulacion", "Simulación", ("simulador-vida", "Simulador de vida"),
            ("simulador-vuelo", "Simulador de vuelo"), ("simulador-conduccion", "Simulador de conducción"),
            ("simulador-granja", "Simulador de granja"), ("simulador-negocios", "Simulador de negocios"),
            ("simulador-construccion", "Simulador de construcción")),
        Entry("gestion-construccion", "Gestión y construcción", ("city-builder", "City builder"),
            ("gestion-colonias", "Gestión de colonias"), ("gestion-recursos", "Gestión de recursos"),
            ("automatizacion-fabricas", "Automatización de fábricas")),
        Entry("puzles-logica", "Puzles y lógica", ("match-3", "Match-3"), ("sokoban", "Sokoban"),
            ("puzles-fisica", "Puzles de física"), ("puzles-espaciales", "Puzles espaciales"),
            ("objetos-ocultos", "Objetos ocultos"), ("escape-room", "Escape room")),
        Entry("terror", "Terror", ("survival-horror", "Survival horror"), ("horror-psicologico", "Horror psicológico"),
            ("horror-accion", "Horror de acción"), ("horror-narrativo", "Horror narrativo"),
            ("horror-cooperativo", "Horror cooperativo")),
        Entry("supervivencia", "Supervivencia", ("survival-crafting", "Survival crafting"),
            ("supervivencia-mundo-abierto", "Supervivencia en mundo abierto"),
            ("supervivencia-zombi", "Supervivencia zombi"), ("supervivencia-espacial", "Supervivencia espacial"),
            ("supervivencia-cooperativa", "Supervivencia cooperativa")),
        Entry("deportes", "Deportes", ("futbol", "Fútbol"), ("baloncesto", "Baloncesto"),
            ("tenis", "Tenis"), ("golf", "Golf"), ("deportes-extremos", "Deportes extremos"),
            ("gestion-deportiva", "Gestión deportiva")),
        Entry("carreras-conduccion", "Carreras y conducción", ("arcade-racing", "Arcade racing"),
            ("sim-racing", "Sim racing"), ("kart-racing", "Kart racing"), ("rally", "Rally"),
            ("street-racing", "Street racing"), ("carreras-futuristas", "Carreras futuristas")),
        Entry("lucha", "Lucha", ("lucha-2d", "Lucha 2D"), ("lucha-3d", "Lucha 3D"),
            ("lucha-arena", "Lucha de arena"), ("lucha-plataformas", "Lucha de plataformas"),
            ("lucha-equipos", "Lucha por equipos")),
        Entry("ritmo-musica", "Ritmo y música", ("baile-ritmico", "Baile rítmico"),
            ("karaoke", "Karaoke"), ("instrumentos-virtuales", "Instrumentos virtuales"),
            ("ritmo-por-notas", "Ritmo por notas")),
        Entry("cartas-juegos-mesa", "Cartas y juegos de mesa", ("deckbuilding", "Deckbuilding"),
            ("cartas-coleccionables", "Cartas coleccionables"), ("juegos-mesa-digitales", "Juegos de mesa digitales"),
            ("juegos-de-dados", "Juegos de dados"), ("ajedrez", "Ajedrez")),
        Entry("party-games", "Party games", ("minijuegos", "Minijuegos"), ("trivia", "Trivia"),
            ("deduccion-social", "Deducción social"), ("dibujo-adivinanzas", "Dibujo y adivinanzas")),
        Entry("roguelike-roguelite", "Roguelike y roguelite", ("roguelike-tradicional", "Roguelike tradicional"),
            ("roguelite-accion", "Roguelite de acción"), ("roguelite-cartas", "Roguelite de cartas"),
            ("roguelite-tactico", "Roguelite táctico")),
        Entry("sandbox-mundo-abierto", "Sandbox y mundo abierto", ("construccion-libre", "Construcción libre"),
            ("sandbox-fisico", "Sandbox físico"), ("mundo-abierto-procedural", "Mundo abierto procedural"),
            ("exploracion-mundo-abierto", "Exploración en mundo abierto")),
        Entry("narrativa-interactiva", "Visual novels y narrativa interactiva",
            ("novela-visual-cinetica", "Novela visual cinética"), ("novela-visual-ramificada", "Novela visual ramificada"),
            ("simulador-citas", "Simulador de citas"), ("narrativa-elecciones", "Narrativa de elecciones")),
        Entry("educacion-creatividad", "Educación y creatividad", ("aprendizaje-idiomas", "Aprendizaje de idiomas"),
            ("juegos-matematicos", "Juegos matemáticos"), ("programacion-educativa", "Programación educativa"),
            ("creacion-artistica", "Creación artística"), ("creacion-niveles", "Creación de niveles")),
        Entry(OtherGenre, "Otros", ("experimentales", "Experimentales"), ("hibridos", "Híbridos"),
            ("sin-subgenero-especifico", "Sin subgénero específico")),
    ];

    public static IEnumerable<GroupGenre> Genres => Entries.Select(entry => entry.Genre);
    public static IEnumerable<GroupSubgenre> Subgenres => Entries.SelectMany(entry => entry.Subgenres);

    public static readonly GroupTag[] Tags =
    [
        new("cooperativo", "Cooperativo", "modalidad"), new("competitivo", "Competitivo", "modalidad"),
        new("un-jugador", "Un jugador", "modalidad"), new("multijugador", "Multijugador", "modalidad"),
        new("pvp", "PvP", "modalidad"), new("pve", "PvE", "modalidad"), new("pvpve", "PvPvE", "modalidad"),
        new("local", "Local", "modalidad"), new("online", "Online", "modalidad"),
        new("indie", "Indie", "estilo"), new("retro", "Retro", "estilo"), new("clasico", "Clásico", "estilo"),
        new("hardcore", "Hardcore", "estilo"), new("casual", "Casual", "estilo"),
        new("speedrunning", "Speedrunning", "estilo"), new("completionist", "Completionist", "estilo"),
        new("coleccionismo", "Coleccionismo", "estilo"), new("pc", "PC", "plataforma"),
        new("playstation", "PlayStation", "plataforma"), new("xbox", "Xbox", "plataforma"),
        new("nintendo", "Nintendo", "plataforma"), new("android", "Android", "plataforma"),
        new("ios", "iOS", "plataforma"), new("retro-gaming", "Retro gaming", "plataforma"),
    ];

    private static (GroupGenre, GroupSubgenre[]) Entry(string id, string name, params (string Id, string Name)[] subgenres)
        => (new(id, name), subgenres.Select(subgenre => new GroupSubgenre(subgenre.Id, id, subgenre.Name,
            subgenre.Id == "rpg-de-accion" ? "ARPG" : null)).ToArray());
}
