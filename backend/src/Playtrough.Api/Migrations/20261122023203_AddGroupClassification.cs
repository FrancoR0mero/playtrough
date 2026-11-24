using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GenreId",
                table: "SocialGroups",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupGenres",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupGenres", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupTags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupTags", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupSubgenres",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    GenreId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Alias = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSubgenres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupSubgenres_GroupGenres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "GroupGenres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupTagAssignments",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupTagAssignments", x => new { x.GroupId, x.TagId });
                    table.ForeignKey(
                        name: "FK_GroupTagAssignments_GroupTags_TagId",
                        column: x => x.TagId,
                        principalTable: "GroupTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupTagAssignments_SocialGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SocialGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupSubgenreAssignments",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    SubgenreId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSubgenreAssignments", x => new { x.GroupId, x.SubgenreId });
                    table.ForeignKey(
                        name: "FK_GroupSubgenreAssignments_GroupSubgenres_SubgenreId",
                        column: x => x.SubgenreId,
                        principalTable: "GroupSubgenres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupSubgenreAssignments_SocialGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SocialGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "GroupGenres",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { "accion", "Acción" },
                    { "aventura", "Aventura" },
                    { "carreras-conduccion", "Carreras y conducción" },
                    { "cartas-juegos-mesa", "Cartas y juegos de mesa" },
                    { "deportes", "Deportes" },
                    { "disparos", "Disparos" },
                    { "educacion-creatividad", "Educación y creatividad" },
                    { "estrategia", "Estrategia" },
                    { "gestion-construccion", "Gestión y construcción" },
                    { "lucha", "Lucha" },
                    { "narrativa-interactiva", "Visual novels y narrativa interactiva" },
                    { "otros", "Otros" },
                    { "party-games", "Party games" },
                    { "plataformas", "Plataformas" },
                    { "puzles-logica", "Puzles y lógica" },
                    { "ritmo-musica", "Ritmo y música" },
                    { "roguelike-roguelite", "Roguelike y roguelite" },
                    { "rpg", "RPG" },
                    { "sandbox-mundo-abierto", "Sandbox y mundo abierto" },
                    { "simulacion", "Simulación" },
                    { "supervivencia", "Supervivencia" },
                    { "terror", "Terror" }
                });

            migrationBuilder.InsertData(
                table: "GroupTags",
                columns: new[] { "Id", "Category", "Name" },
                values: new object[,]
                {
                    { "android", "plataforma", "Android" },
                    { "casual", "estilo", "Casual" },
                    { "clasico", "estilo", "Clásico" },
                    { "coleccionismo", "estilo", "Coleccionismo" },
                    { "competitivo", "modalidad", "Competitivo" },
                    { "completionist", "estilo", "Completionist" },
                    { "cooperativo", "modalidad", "Cooperativo" },
                    { "hardcore", "estilo", "Hardcore" },
                    { "indie", "estilo", "Indie" },
                    { "ios", "plataforma", "iOS" },
                    { "local", "modalidad", "Local" },
                    { "multijugador", "modalidad", "Multijugador" },
                    { "nintendo", "plataforma", "Nintendo" },
                    { "online", "modalidad", "Online" },
                    { "pc", "plataforma", "PC" },
                    { "playstation", "plataforma", "PlayStation" },
                    { "pve", "modalidad", "PvE" },
                    { "pvp", "modalidad", "PvP" },
                    { "pvpve", "modalidad", "PvPvE" },
                    { "retro", "estilo", "Retro" },
                    { "retro-gaming", "plataforma", "Retro gaming" },
                    { "speedrunning", "estilo", "Speedrunning" },
                    { "un-jugador", "modalidad", "Un jugador" },
                    { "xbox", "plataforma", "Xbox" }
                });

            migrationBuilder.InsertData(
                table: "GroupSubgenres",
                columns: new[] { "Id", "Alias", "GenreId", "Name" },
                values: new object[,]
                {
                    { "4x", null, "estrategia", "4X" },
                    { "ajedrez", null, "cartas-juegos-mesa", "Ajedrez" },
                    { "aprendizaje-idiomas", null, "educacion-creatividad", "Aprendizaje de idiomas" },
                    { "arcade-racing", null, "carreras-conduccion", "Arcade racing" },
                    { "arena-shooter", null, "disparos", "Arena shooter" },
                    { "auto-battler", null, "estrategia", "Auto battler" },
                    { "automatizacion-fabricas", null, "gestion-construccion", "Automatización de fábricas" },
                    { "aventura-exploracion", null, "aventura", "Aventura de exploración" },
                    { "aventura-grafica", null, "aventura", "Aventura gráfica" },
                    { "aventura-textual", null, "aventura", "Aventura textual" },
                    { "baile-ritmico", null, "ritmo-musica", "Baile rítmico" },
                    { "baloncesto", null, "deportes", "Baloncesto" },
                    { "beat-em-up", null, "accion", "Beat 'em up" },
                    { "boomer-shooter", null, "disparos", "Boomer shooter" },
                    { "bullet-hell", null, "disparos", "Bullet hell" },
                    { "carreras-futuristas", null, "carreras-conduccion", "Carreras futuristas" },
                    { "cartas-coleccionables", null, "cartas-juegos-mesa", "Cartas coleccionables" },
                    { "character-action", null, "accion", "Character action" },
                    { "cinematic-platformer", null, "plataformas", "Cinematic platformer" },
                    { "city-builder", null, "gestion-construccion", "City builder" },
                    { "collectathon", null, "plataformas", "Collectathon" },
                    { "construccion-libre", null, "sandbox-mundo-abierto", "Construcción libre" },
                    { "creacion-artistica", null, "educacion-creatividad", "Creación artística" },
                    { "creacion-niveles", null, "educacion-creatividad", "Creación de niveles" },
                    { "crpg", null, "rpg", "CRPG" },
                    { "deckbuilding", null, "cartas-juegos-mesa", "Deckbuilding" },
                    { "deduccion-social", null, "party-games", "Deducción social" },
                    { "deportes-extremos", null, "deportes", "Deportes extremos" },
                    { "dibujo-adivinanzas", null, "party-games", "Dibujo y adivinanzas" },
                    { "dungeon-crawler", null, "rpg", "Dungeon crawler" },
                    { "endless-runner", null, "plataformas", "Endless runner" },
                    { "escape-room", null, "puzles-logica", "Escape room" },
                    { "estrategia-tactica", null, "estrategia", "Estrategia táctica" },
                    { "experimentales", null, "otros", "Experimentales" },
                    { "exploracion-mundo-abierto", null, "sandbox-mundo-abierto", "Exploración en mundo abierto" },
                    { "extraction-shooter", null, "disparos", "Extraction shooter" },
                    { "fps", null, "disparos", "FPS" },
                    { "futbol", null, "deportes", "Fútbol" },
                    { "gestion-colonias", null, "gestion-construccion", "Gestión de colonias" },
                    { "gestion-deportiva", null, "deportes", "Gestión deportiva" },
                    { "gestion-recursos", null, "gestion-construccion", "Gestión de recursos" },
                    { "golf", null, "deportes", "Golf" },
                    { "gran-estrategia", null, "estrategia", "Gran estrategia" },
                    { "hack-and-slash", null, "accion", "Hack and slash" },
                    { "hero-shooter", null, "disparos", "Hero shooter" },
                    { "hibridos", null, "otros", "Híbridos" },
                    { "horror-accion", null, "terror", "Horror de acción" },
                    { "horror-cooperativo", null, "terror", "Horror cooperativo" },
                    { "horror-narrativo", null, "terror", "Horror narrativo" },
                    { "horror-psicologico", null, "terror", "Horror psicológico" },
                    { "instrumentos-virtuales", null, "ritmo-musica", "Instrumentos virtuales" },
                    { "jrpg", null, "rpg", "JRPG" },
                    { "juegos-de-dados", null, "cartas-juegos-mesa", "Juegos de dados" },
                    { "juegos-matematicos", null, "educacion-creatividad", "Juegos matemáticos" },
                    { "juegos-mesa-digitales", null, "cartas-juegos-mesa", "Juegos de mesa digitales" },
                    { "karaoke", null, "ritmo-musica", "Karaoke" },
                    { "kart-racing", null, "carreras-conduccion", "Kart racing" },
                    { "looter-shooter", null, "disparos", "Looter shooter" },
                    { "lucha-2d", null, "lucha", "Lucha 2D" },
                    { "lucha-3d", null, "lucha", "Lucha 3D" },
                    { "lucha-arena", null, "lucha", "Lucha de arena" },
                    { "lucha-equipos", null, "lucha", "Lucha por equipos" },
                    { "lucha-plataformas", null, "lucha", "Lucha de plataformas" },
                    { "match-3", null, "puzles-logica", "Match-3" },
                    { "metroidvania", null, "plataformas", "Metroidvania" },
                    { "minijuegos", null, "party-games", "Minijuegos" },
                    { "mmorpg", null, "rpg", "MMORPG" },
                    { "moba", null, "estrategia", "MOBA" },
                    { "monster-collecting", null, "rpg", "Monster collecting" },
                    { "mundo-abierto-procedural", null, "sandbox-mundo-abierto", "Mundo abierto procedural" },
                    { "musou", null, "accion", "Musou" },
                    { "narrativa-elecciones", null, "narrativa-interactiva", "Narrativa de elecciones" },
                    { "novela-visual-cinetica", null, "narrativa-interactiva", "Novela visual cinética" },
                    { "novela-visual-ramificada", null, "narrativa-interactiva", "Novela visual ramificada" },
                    { "objetos-ocultos", null, "puzles-logica", "Objetos ocultos" },
                    { "parkour", null, "accion", "Parkour" },
                    { "point-and-click", null, "aventura", "Point and click" },
                    { "precision-platformer", null, "plataformas", "Precision platformer" },
                    { "programacion-educativa", null, "educacion-creatividad", "Programación educativa" },
                    { "puzles-espaciales", null, "puzles-logica", "Puzles espaciales" },
                    { "puzles-fisica", null, "puzles-logica", "Puzles de física" },
                    { "puzzle-platformer", null, "plataformas", "Puzzle platformer" },
                    { "rally", null, "carreras-conduccion", "Rally" },
                    { "ritmo-por-notas", null, "ritmo-musica", "Ritmo por notas" },
                    { "roguelike-tradicional", null, "roguelike-roguelite", "Roguelike tradicional" },
                    { "roguelite-accion", null, "roguelike-roguelite", "Roguelite de acción" },
                    { "roguelite-cartas", null, "roguelike-roguelite", "Roguelite de cartas" },
                    { "roguelite-tactico", null, "roguelike-roguelite", "Roguelite táctico" },
                    { "rpg-de-accion", "ARPG", "rpg", "RPG de acción" },
                    { "rpg-narrativo", null, "rpg", "RPG narrativo" },
                    { "rpg-por-turnos", null, "rpg", "RPG por turnos" },
                    { "rts", null, "estrategia", "RTS" },
                    { "run-and-gun", null, "accion", "Run and gun" },
                    { "sandbox-fisico", null, "sandbox-mundo-abierto", "Sandbox físico" },
                    { "sigilo", null, "accion", "Sigilo" },
                    { "sim-racing", null, "carreras-conduccion", "Sim racing" },
                    { "simulador-citas", null, "narrativa-interactiva", "Simulador de citas" },
                    { "simulador-conduccion", null, "simulacion", "Simulador de conducción" },
                    { "simulador-construccion", null, "simulacion", "Simulador de construcción" },
                    { "simulador-granja", null, "simulacion", "Simulador de granja" },
                    { "simulador-negocios", null, "simulacion", "Simulador de negocios" },
                    { "simulador-vida", null, "simulacion", "Simulador de vida" },
                    { "simulador-vuelo", null, "simulacion", "Simulador de vuelo" },
                    { "sin-subgenero-especifico", null, "otros", "Sin subgénero específico" },
                    { "sokoban", null, "puzles-logica", "Sokoban" },
                    { "soulslike", null, "rpg", "Soulslike" },
                    { "street-racing", null, "carreras-conduccion", "Street racing" },
                    { "supervivencia-cooperativa", null, "supervivencia", "Supervivencia cooperativa" },
                    { "supervivencia-espacial", null, "supervivencia", "Supervivencia espacial" },
                    { "supervivencia-mundo-abierto", null, "supervivencia", "Supervivencia en mundo abierto" },
                    { "supervivencia-zombi", null, "supervivencia", "Supervivencia zombi" },
                    { "survival-crafting", null, "supervivencia", "Survival crafting" },
                    { "survival-horror", null, "terror", "Survival horror" },
                    { "tactical-rpg", null, "rpg", "Tactical RPG" },
                    { "tactical-shooter", null, "disparos", "Tactical shooter" },
                    { "tbs", null, "estrategia", "TBS" },
                    { "tenis", null, "deportes", "Tenis" },
                    { "tower-defense", null, "estrategia", "Tower defense" },
                    { "tps", null, "disparos", "TPS" },
                    { "trivia", null, "party-games", "Trivia" },
                    { "wrpg", null, "rpg", "WRPG" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialGroups_DeletedAt_GenreId_Id",
                table: "SocialGroups",
                columns: new[] { "DeletedAt", "GenreId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialGroups_GenreId",
                table: "SocialGroups",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupGenres_Name",
                table: "GroupGenres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupSubgenreAssignments_SubgenreId_GroupId",
                table: "GroupSubgenreAssignments",
                columns: new[] { "SubgenreId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupSubgenres_GenreId",
                table: "GroupSubgenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupSubgenres_Name",
                table: "GroupSubgenres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupTagAssignments_TagId_GroupId",
                table: "GroupTagAssignments",
                columns: new[] { "TagId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupTags_Name",
                table: "GroupTags",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialGroups_GroupGenres_GenreId",
                table: "SocialGroups",
                column: "GenreId",
                principalTable: "GroupGenres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialGroups_GroupGenres_GenreId",
                table: "SocialGroups");

            migrationBuilder.DropTable(
                name: "GroupSubgenreAssignments");

            migrationBuilder.DropTable(
                name: "GroupTagAssignments");

            migrationBuilder.DropTable(
                name: "GroupSubgenres");

            migrationBuilder.DropTable(
                name: "GroupTags");

            migrationBuilder.DropTable(
                name: "GroupGenres");

            migrationBuilder.DropIndex(
                name: "IX_SocialGroups_DeletedAt_GenreId_Id",
                table: "SocialGroups");

            migrationBuilder.DropIndex(
                name: "IX_SocialGroups_GenreId",
                table: "SocialGroups");

            migrationBuilder.DropColumn(
                name: "GenreId",
                table: "SocialGroups");
        }
    }
}
