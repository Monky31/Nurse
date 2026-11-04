using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JuegoTerror
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Juego());
        }
    }

    enum Estado { Titulo, Jugando, Descubierto }

    class Juego : Form
    {
        const int T = 40, W = 24, H = 16;   // tile 40px, cada habitación es de 24x16 -> 960x640
        const float TiempoParte = 60f, TiempoCuerpo = 30f;   // segundos que duran las partes sueltas y los cuerpos antes de desaparecer

        // ================= CLASES DEL MUNDO =================
        class Objeto { public string Nombre; public PointF Pos; public float Edad; public Objeto(string n, PointF p) { Nombre = n; Pos = p; } }
        class Npc
        {
            public string Tipo;
            public PointF Pos, Casa;
            public Color Bata;
            public string[] Frases;
            public int Idx;
            public bool Cortado, TieneDestino, Sangrando, Huye, Alerta, Sigue, HuyeDelCanibal;
            public int Extremidades, Confiabilidad, Variante;
            public float Fase, Espera, Vel, VelBase, SangreTimer, Repath, TiempoMuerto, Grito;
            public Point Destino, Paso;
        }
        // Figura que solo existe en tu cabeza cuando la cordura está por los suelos
        class Alucinacion
        {
            public PointF Pos;
            public float Fase, Vida, Vel, Susurro;
        }
        class Cortable { public string Tipo, Nombre, Herramienta, Revela, Texto; public PointF Pos; public bool Cortado; }
        class Puerta { public Point Tile; public int Destino; public PointF Spawn; public string Requiere; public Cortable Atada; public bool Abierta; }

        class Cuarto
        {
            public string Nombre;
            public string[] Mapa;
            public Brush Suelo1, Suelo2, Pared;
            public List<Puerta> Puertas = new List<Puerta>();
            public List<Objeto> Objetos = new List<Objeto>();
            public List<Npc> Npcs = new List<Npc>();
            public List<Cortable> Cortables = new List<Cortable>();
            public List<(PointF Pos, float Radio)> Luces = new List<(PointF, float)>();
            public List<RectangleF> Manchas = new List<RectangleF>();
            public int ManchasFijas;    // manchas de decoración que no se secan
            public float Secado;        // temporizador para que la sangre vieja se vaya secando
            public Bitmap Fondo;        // suelo, paredes y muebles dibujados una sola vez
            public bool Exterior;       // césped en vez de baldosas
            public Color Tinte;         // color de ambiente de la habitación

            // Solo el '.' es suelo libre. Todo lo demás (pared, cama, mesa, puerta...) bloquea el paso
            public bool Solido(int tx, int ty) => tx < 0 || ty < 0 || tx >= W || ty >= H || Mapa[ty][tx] != '.';
            // Lo que bloquea la luz y la vista: paredes y puertas
            public bool Opaco(int tx, int ty) => tx < 0 || ty < 0 || tx >= W || ty >= H || Mapa[ty][tx] == '#' || Mapa[ty][tx] == 'D';
        }

        // ================= PINCELES Y COLORES =================
        static readonly Brush brSangre = new SolidBrush(Color.FromArgb(110, 10, 10));
        static readonly Brush brOscuro = new SolidBrush(Color.FromArgb(142, 0, 0, 0));
        static readonly Brush brPiel = new SolidBrush(Color.FromArgb(230, 200, 180));
        static readonly Brush brMesa = new SolidBrush(Color.FromArgb(110, 75, 45));
        static readonly Brush brMetal = new SolidBrush(Color.FromArgb(120, 135, 145));
        static readonly Brush brAcero = new SolidBrush(Color.FromArgb(170, 185, 195));
        static readonly Brush brCarton = new SolidBrush(Color.FromArgb(165, 125, 70));
        static readonly Brush brColchon = new SolidBrush(Color.FromArgb(210, 218, 228));
        static readonly Brush brPuerta = new SolidBrush(Color.FromArgb(120, 80, 50));
        static readonly Pen penPared = new Pen(Color.FromArgb(90, 100, 100), 2);
        static readonly Pen penMueble = new Pen(Color.FromArgb(40, 35, 35), 2);
        static readonly Pen penBrazo = new Pen(Color.FromArgb(205, 205, 215), 4);
        static readonly Pen penVenda = new Pen(Color.FromArgb(235, 235, 225), 5);
        static readonly Pen penCinta = new Pen(Color.FromArgb(220, 190, 120), 3);
        static readonly Pen penAcero = new Pen(Color.Silver, 3);
        static readonly Pen penHilo = new Pen(Color.FromArgb(190, 175, 160), 1.5f);
        static readonly Pen penHueso = new Pen(Color.FromArgb(225, 220, 200), 2.5f);
        static readonly Brush brSombra = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        static readonly Brush brSombraSuave = new SolidBrush(Color.FromArgb(38, 0, 0, 0));
        static readonly Brush brSangreViva = new SolidBrush(Color.FromArgb(170, 150, 14, 14));
        static readonly Brush brOjera = new SolidBrush(Color.FromArgb(130, 45, 30, 55));
        static readonly Brush brPeloEnf = new SolidBrush(Color.FromArgb(70, 45, 35));
        static readonly Pen penJunta = new Pen(Color.FromArgb(60, 0, 0, 0), 1);
        static readonly Pen penBrilloSuelo = new Pen(Color.FromArgb(14, 255, 255, 255), 1);
        static readonly Pen penBiselClaro = new Pen(Color.FromArgb(40, 255, 255, 255), 1);
        static readonly Pen penBiselOscuro = new Pen(Color.FromArgb(70, 0, 0, 0), 1);
        static readonly Pen penGrieta = new Pen(Color.FromArgb(110, 20, 20, 20), 1);
        static readonly Pen penRibete = new Pen(Color.FromArgb(120, 70, 130, 190), 2);
        static readonly Pen penPanel = new Pen(Color.FromArgb(70, 255, 255, 255), 1);
        static readonly Color[] Pieles = { Color.FromArgb(232, 205, 185), Color.FromArgb(205, 165, 135), Color.FromArgb(150, 105, 80), Color.FromArgb(225, 215, 205) };
        static readonly Color[] Pelos = { Color.FromArgb(30, 25, 22), Color.FromArgb(70, 45, 28), Color.FromArgb(150, 130, 100), Color.FromArgb(185, 185, 190), Color.FromArgb(110, 40, 30) };
        static readonly Bitmap Vineta = ConstruirVineta();   // oscurece los bordes de la pantalla
        static readonly Bitmap Ruido = ConstruirRuido();     // grano de película

        static Bitmap ConstruirVineta()
        {
            var bmp = new Bitmap(W * T, H * T);
            using var g = Graphics.FromImage(bmp);
            using var path = new GraphicsPath();
            path.AddEllipse(-W * T * 0.25f, -H * T * 0.30f, W * T * 1.5f, H * T * 1.6f);
            using var pg = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(0, 0, 0, 0),
                SurroundColors = new[] { Color.FromArgb(150, 0, 0, 0) },
                CenterPoint = new PointF(W * T / 2f, H * T / 2f),
                FocusScales = new PointF(0.55f, 0.55f)
            };
            g.FillRectangle(pg, 0, 0, W * T, H * T);
            return bmp;
        }

        static Bitmap ConstruirRuido()
        {
            var bmp = new Bitmap(160, 160);
            var r = new Random(5);
            for (int y = 0; y < 160; y++)
                for (int x = 0; x < 160; x++)
                {
                    int v = r.Next(256);
                    bmp.SetPixel(x, y, Color.FromArgb(r.Next(0, 26), v, v, v));
                }
            return bmp;
        }

        static readonly Color Sabana = Color.FromArgb(190, 205, 225);
        static readonly Color SabanaMorgue = Color.FromArgb(225, 225, 230);
        static readonly Color Verde = Color.FromArgb(80, 150, 130);
        static readonly Color Bata = Color.FromArgb(120, 160, 200);
        static readonly Color Gris = Color.FromArgb(150, 150, 160);
        static readonly Color[] BatasNpc = { Bata, Gris, Verde, Color.FromArgb(190, 150, 120) };
        static readonly string[][] FrasesNuevos =
        {
            new[] { "¿Sabes dónde está mi habitación?", "Creo que me perdí..." },
            new[] { "Hace frío aquí, ¿verdad?", "¿Eres la enfermera de turno?" },
            new[] { "No me siento bien...", "¿Puedes ayudarme?" },
        };

        // Lo que dice la enfermera según su estado mental. Cambia al alimentar al caníbal.
        static readonly string[] DialogosNormal =
        {
            "Buenas noches... ¿te encuentras bien?",
            "No deberías estar levantado a estas horas.",
            "¿Has oído eso? Viene del quirófano.",
            "Tranquilo. Solo vengo a hacer mi ronda.",
        };
        static readonly string[] DialogosIntermedio =
        {
            "Todavía puedo oler la sangre en mis manos.",
            "El ser tiene hambre... y tú hueles tan fresco.",
            "No me mires así. Conozco bien el filo de un bisturí.",
            "Esta noche todo el mundo parece carne.",
        };
        static readonly string[] DialogosFinal =
        {
            "Ya no hay vuelta atrás. La carne es carne.",
            "No grites. Solo quiero oír cómo se rompe.",
            "El ser y yo compartimos el mismo hambre.",
            "Quédate quieto. Será rápido... o no.",
        };

        // ================= ESTADO DEL JUEGO =================
        Estado estado = Estado.Titulo;
        readonly Random rnd = new Random();
        readonly Stopwatch reloj = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        readonly HashSet<Keys> teclas = new HashSet<Keys>();
        double ultimo;
        float tiempo, tiempoPartida;

        List<Cuarto> cuartos;
        int actual;                                  // habitación (escena) en la que estás
        Cuarto cuarto => cuartos[actual];

        // Enfermera
        float px, py, angulo, stamina;
        bool linterna, corriendo, cansado;
        string mano;                                 // objeto que lleva en la mano (null = vacía)
        bool armaSangrienta;                         // el filo que llevas está manchado de sangre
        object objetivo;                             // con qué puede interactuar ahora mismo
        string prompt = "";

        // Transición entre habitaciones y efectos
        float fade, tiempoNombre, parpadeo, sacudida, eventoAmbiente, tiempoMensaje;
        int faseFade;                                // 0 nada, 1 oscureciendo, 2 aclarando
        int alertados;                               // NPCs que te han visto cortar a alguien
        int partesServidas;                          // partes que le has dado al caníbal
        bool llaveSoltada;                           // el caníbal ya escupió la llave del exterior
        float tiempoSpawn, policiaTimer;             // aparición de gente nueva y de policías
        bool policiaLlamada, policiaLlego;           // con 10 alertas alguien llama a la policía
        int[] poblacionInicial;                      // cuánta gente había al empezar en cada habitación (para reaparecer)

        // Caníbal: se intranquiliza tras 2 partes y, si no le das de comer a tiempo, se levanta a cazar
        Npc canibalRef;
        int cuartoCanibal;                           // habitación en la que está el caníbal
        bool hambriento, canibalLibre, avisoHambre;
        float hambre, canibalEspera, canibalComiendo, latidoCanibal, cercaniaCanibal;
        PointF entrada;                              // por dónde entraste a la habitación actual
        string tituloPerder = "TE DETUVO LA POLICÍA";
        readonly List<float[]> polvo = new List<float[]>();   // motas de polvo: x, y, vx, vy, fase, tamaño
        Npc hablando;                                // NPC con el que estás conversando
        string motivoPerder = "Demasiados testigos. El hospital ya sabe lo que hiciste.";
        Puerta puertaUsada;
        string mensaje = "";

        // ================= CORDURA, PASTILLAS Y CARTAS DEL SER =================
        const int AlertasMax = 10;
        const float CorduraMax = 100f;
        float cordura;                               // 0 = enloquecido
        bool enloquecido;
        float ataqueLocura, tiempoPastillas;
        // Mejoras que se obtienen al elegir cartas cada 5 partes
        float staminaMax, velJugador, extraTiempoHambre;
        int confianzaExtra;
        bool silencioAlCortar, canibalLento, evitarAlerta50, masPastillas;
        bool eligiendoCarta;
        readonly List<int> cartasActuales = new List<int>();

        // Alucinaciones: aparecen con la cordura crítica y te matan si te tocan
        readonly List<Alucinacion> alucinaciones = new List<Alucinacion>();
        float tiempoAlucinacion, calmaPastillas;
        bool yaAvisoAlucinacion;
        // Dones nuevos
        int calmaMental, segundoAliento;
        bool ojoDelSer, sangreFria, corteLimpio, estomagoNegro, pasoFantasma, pastillasFuertes, veloMental;

        // ================= DIÁLOGOS, NOMBRE E IMÁGENES =================
        string nombreJugador = "";
        readonly List<(bool EsJugador, string Texto)> dialogo = new List<(bool, string)>();
        int dialogoIdx;
        Bitmap imgDialogoNormal, imgDialogoIntermedio, imgDialogoFinal, imgTexto;
        readonly List<(int Cuarto, Npc Npc)> npcsOcultos = new List<(int, Npc)>();
        bool conocioCanibal;

        public Juego()
        {
            Text = "Turno de Noche";
            ClientSize = new Size(W * T, H * T);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Cursor = Cursors.Default;

            for (int i = 0; i < 60; i++)
                polvo.Add(new[] { (float)rnd.NextDouble() * W * T, (float)rnd.NextDouble() * H * T, (float)rnd.NextDouble() * 8 - 4,
                                  -1f - (float)rnd.NextDouble() * 5, (float)rnd.NextDouble() * 6, 1.5f + (float)rnd.NextDouble() * 1.8f });

            CargarImagenesDialogo();
            Reiniciar();
            timer.Tick += (s, e) =>
            {
                double ahora = reloj.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(ahora - ultimo, 0.05);
                ultimo = ahora;
                Actualizar(dt);
                Invalidate();
            };
            timer.Start();
        }

        // ================= CARGA DE LAS IMÁGENES DE DIÁLOGO =================
        void CargarImagenesDialogo()
        {
            imgDialogoNormal = CargarImagenDialogo("EnfermeraDialogo.png");
            imgDialogoIntermedio = CargarImagenDialogo("EnfermeraDialogoIntermedio.png");
            imgDialogoFinal = CargarImagenDialogo("EnfermeraDialogoFinal.png");
            imgTexto = CargarImagenDialogo("Texto.png");
        }

        static Bitmap CargarImagenDialogo(string archivo)
        {
            var carpetas = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "Dialogos"),
                Path.Combine(Environment.CurrentDirectory, "Dialogos")
            };

            // También se busca subiendo desde la carpeta del ejecutable (por si se lanza desde bin)
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                carpetas.Add(Path.Combine(dir.FullName, "Dialogos"));

            foreach (string carpeta in carpetas)
            {
                string ruta = Path.Combine(carpeta, archivo);
                if (!File.Exists(ruta)) continue;
                try
                {
                    using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read);
                    using var temp = Image.FromStream(fs);
                    return new Bitmap(temp);
                }
                catch { }
            }
            return null;
        }

        Bitmap ImagenEnfermeraDialogo()
        {
            if (partesServidas >= 10) return imgDialogoFinal ?? imgDialogoIntermedio ?? imgDialogoNormal;
            if (partesServidas >= 1) return imgDialogoIntermedio ?? imgDialogoNormal;
            return imgDialogoNormal;
        }

        // ================= CREACIÓN DE HABITACIONES =================
        static PointF C(int tx, int ty) => new PointF(tx * T + T / 2f, ty * T + T / 2f);             // centro de un tile
        static PointF Cama(int tx, int ty) => new PointF((tx + 1) * T, ty * T + T / 2f);             // centro de algo de 2 tiles de ancho
        static Brush B(int r, int g, int b) => new SolidBrush(Color.FromArgb(r, g, b));
        static Npc N(string tipo, PointF pos, Color bata, params string[] frases)
        {
            float v = tipo == "sentado" ? 34f : 58f;
            return new Npc
            {
                Tipo = tipo, Pos = pos, Casa = pos, Bata = bata, Frases = frases,
                VelBase = v, Vel = v, Espera = 0.4f, Extremidades = 4, Confiabilidad = 1, Variante = ((int)pos.X * 31 + (int)pos.Y * 17) & 0x7fffffff
            };
        }

        // Mapa: '#' pared, '.' suelo, 'D' puerta, 'b' cama, 'm' mesa, 'a' armario/estante, 'o' mesa de operaciones,
        //       'z' plancha de la morgue, 'k' silla, 'c' caja de cartón
        static List<Cuarto> Construir()
        {
            // ===== 0: HABITACIÓN 12 (aquí despiertas) =====
            var vendas = new Cortable
            {
                Tipo = "vendas", Nombre = "las vendas", Herramienta = "Tijeras", Pos = C(23, 7),
                Texto = "Cortas las vendas. La puerta quedó libre."
            };
            var hab = new Cuarto
            {
                Nombre = "Habitación 12",
                Suelo1 = B(92, 104, 108), Suelo2 = B(84, 95, 99), Pared = B(135, 150, 150),
                Mapa = new[]
                {
                    "########################",
                    "#......................#",
                    "#.bb.m.......bb.m......#",
                    "#......................#",
                    "#......................#",
                    "#.bb...................#",
                    "#......................#",
                    "#......................D",
                    "#......................#",
                    "#......................#",
                    "#.bb.......bb..........#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "########################",
                }
            };
            hab.Cortables.Add(vendas);
            hab.Puertas.Add(new Puerta { Tile = new Point(23, 7), Destino = 1, Spawn = C(1, 7), Atada = vendas });
            hab.Objetos.Add(new Objeto("Tijeras", C(16, 2)));
            hab.Objetos.Add(new Objeto("Pastillas de cordura", C(13, 7)));
            hab.Luces.Add((C(16, 2), 95f));   // lámpara de la mesita
            hab.Npcs.Add(N("dormido", Cama(2, 2), Sabana, "Respira muy despacio. No despierta.", "Su piel está helada."));
            hab.Npcs.Add(N("dormido", Cama(13, 2), Sabana, "Duerme... o eso parece.", "Murmura algo en sueños."));
            hab.Npcs.Add(N("dormido", Cama(2, 10), Sabana, "No se mueve. Ni siquiera respira.", "Mejor no lo despiertes."));
            hab.Npcs.Add(N("dormido", Cama(11, 10), Sabana, "Tiene los ojos abiertos, pero no ve nada."));
            hab.Npcs.Add(N("parado", C(20, 12), Bata, "Shhh... no hagas ruido. Están dormidos.", "Tú también deberías dormir, enfermera.", "Aquí las tijeras sirven para más que cortar gasas."));

            // ===== 1: PASILLO (el centro del hospital) =====
            var pas = new Cuarto
            {
                Nombre = "Pasillo central",
                Suelo1 = B(98, 108, 100), Suelo2 = B(88, 98, 90), Pared = B(140, 150, 140),
                Mapa = new[]
                {
                    "######D##########D######",
                    "#......................#",
                    "#......................#",
                    "#..kkk..........kkk....#",
                    "#......................#",
                    "#......................#",
                    "#...mmmmmm.............#",
                    "D......................D",
                    "#......................#",
                    "#......................#",
                    "#..................bb..#",
                    "#......................#",
                    "#..kk..................#",
                    "#......................#",
                    "#......................#",
                    "############D###########",
                }
            };
            pas.Puertas.Add(new Puerta { Tile = new Point(0, 7), Destino = 0, Spawn = C(22, 7) });
            pas.Puertas.Add(new Puerta { Tile = new Point(6, 0), Destino = 2, Spawn = C(6, 14) });
            pas.Puertas.Add(new Puerta { Tile = new Point(17, 0), Destino = 3, Spawn = C(17, 14) });
            pas.Puertas.Add(new Puerta { Tile = new Point(12, 15), Destino = 4, Spawn = C(12, 1), Requiere = "Llave de la morgue" });
            pas.Puertas.Add(new Puerta { Tile = new Point(23, 7), Destino = 5, Spawn = C(1, 7), Requiere = "Llave del exterior" });
            pas.Objetos.Add(new Objeto("Jeringa", C(8, 12)));
            pas.Objetos.Add(new Objeto("Pastillas de cordura", C(20, 8)));
            pas.Npcs.Add(N("parado", C(9, 3), Bata, "Llevo horas esperando... ¿ya es mi turno?", "No mires atrás."));
            pas.Npcs.Add(N("sentado", C(4, 3), Gris, "...", "La salida necesita la llave del exterior."));
            pas.Npcs.Add(N("sentado", C(17, 3), Gris, "Ella no debería estar despierta.", "Se lo llevó hacia la morgue..."));
            pas.Npcs.Add(N("parado", C(14, 9), Bata, "Mira cómo parpadean las luces.", "Cuando parpadean... él está cerca."));
            pas.Npcs.Add(N("dormido", Cama(19, 10), Gris, "Está en una camilla. No sabes si sigue vivo."));

            // ===== 2: QUIRÓFANO =====
            var quir = new Cuarto
            {
                Nombre = "Quirófano",
                Suelo1 = B(80, 110, 105), Suelo2 = B(72, 100, 96), Pared = B(120, 150, 145),
                Mapa = new[]
                {
                    "########################",
                    "#.aaaaaa...........aaaa#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#........ooo...........#",
                    "#........ooo...m.......#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#.aa...................#",
                    "#......................#",
                    "#......................#",
                    "######D#################",
                }
            };
            quir.Puertas.Add(new Puerta { Tile = new Point(6, 15), Destino = 1, Spawn = C(6, 1) });
            quir.Objetos.Add(new Objeto("Bisturí", C(15, 6)));
            quir.Luces.Add((new PointF(420, 240), 120f));   // lámpara quirúrgica
            quir.Npcs.Add(N("dormido", new PointF(420, 240), Verde, "Está abierto de arriba abajo... y respira.", "No deberías ver esto."));

            // El caníbal vive en el quirófano: cada parte que come lo hace más grande y más tétrico
            quir.Luces.Add((C(17, 9), 100f));
            quir.Npcs.Add(N("destrozado", C(17, 9), Color.FromArgb(120, 60, 60),
                "Hambre...", "Más carne...", "Ven... ven...", "Te conozco..."));
            var comido = N("comido", C(14, 11), Color.FromArgb(110, 55, 55), "");
            comido.Cortado = true;   // es un cuerpo: no se mueve ni bloquea el paso
            quir.Npcs.Add(comido);

            // ===== 3: FARMACIA =====
            var caja = new Cortable
            {
                Tipo = "caja", Nombre = "la caja sellada", Herramienta = "cualquiera", Pos = C(16, 10), Revela = "Llave de la morgue",
                Texto = "Abres la caja: dentro hay una llave con la etiqueta \"Morgue\"."
            };
            var farm = new Cuarto
            {
                Nombre = "Farmacia",
                Suelo1 = B(105, 100, 88), Suelo2 = B(95, 90, 78), Pared = B(150, 145, 125),
                Mapa = new[]
                {
                    "########################",
                    "#.aaaaaaaaaaaaaaaaaaaa.#",
                    "#......................#",
                    "#.aaaa.....aaaa.....aa.#",
                    "#.aaaa.....aaaa.....aa.#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#....mmm...............#",
                    "#......................#",
                    "#...............c......#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#################D######",
                }
            };
            farm.Cortables.Add(caja);
            farm.Puertas.Add(new Puerta { Tile = new Point(17, 15), Destino = 1, Spawn = C(17, 1) });
            farm.Objetos.Add(new Objeto("Frasco de pastillas", C(6, 8)));
            farm.Npcs.Add(N("parado", C(8, 12), Bata, "Necesito mis pastillas... ¿tú tienes pastillas?", "Cuéntalas... siempre faltan."));
            farm.Npcs.Add(N("parado", C(20, 12), Bata, "Dicen que la llave de la morgue está en la caja de cartón.", "Pero la cinta es muy gruesa..."));

            // ===== 4: MORGUE =====
            var bolsa = new Cortable
            {
                Tipo = "bolsa", Nombre = "la bolsa negra", Herramienta = "Bisturí", Pos = Cama(19, 9), Revela = "Cuchillo de enfermería",
                Texto = "Rasgas la bolsa negra. Algo cae al suelo: un cuchillo de enfermería."
            };
            var morg = new Cuarto
            {
                Nombre = "Morgue",
                Suelo1 = B(75, 85, 95), Suelo2 = B(68, 78, 88), Pared = B(110, 125, 140),
                Mapa = new[]
                {
                    "############D###########",
                    "#......................#",
                    "#.aaaaaa.......aaaaaa..#",
                    "#.aaaaaa.......aaaaaa..#",
                    "#......................#",
                    "#......................#",
                    "#....zz.....zz.....zz..#",
                    "#......................#",
                    "#......................#",
                    "#....zz.....zz.....zz..#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "########################",
                }
            };
            morg.Cortables.Add(bolsa);
            morg.Puertas.Add(new Puerta { Tile = new Point(12, 0), Destino = 1, Spawn = C(12, 14) });
            morg.Npcs.Add(N("dormido", Cama(5, 6), SabanaMorgue, "Está frío. Muy frío."));
            morg.Npcs.Add(N("dormido", Cama(12, 6), SabanaMorgue, "La etiqueta del pie dice: Paciente 13."));
            morg.Npcs.Add(N("dormido", Cama(19, 6), SabanaMorgue, "No debería moverse... ¿verdad?"));
            morg.Npcs.Add(N("dormido", Cama(5, 9), SabanaMorgue, "Su mano está cerrada con fuerza."));
            morg.Npcs.Add(N("dormido", Cama(12, 9), SabanaMorgue, "Huele a formol."));
            morg.Npcs.Add(N("parado", C(3, 13), Bata, "Hay un cuchillo dentro de la bolsa negra.", "Yo no la toqué... lo juro."));

            // ===== 5: AFUERA DEL HOSPITAL =====
            var fuera = new Cuarto
            {
                Nombre = "Afuera del hospital",
                Exterior = true,
                Suelo1 = B(45, 48, 52), Suelo2 = B(38, 41, 45), Pared = B(60, 70, 65),
                Mapa = new[]
                {
                    "########################",
                    "#......................#",
                    "#....c...........c.....#",
                    "#......................#",
                    "#......................#",
                    "#..............c.......#",
                    "#......................#",
                    "D......................#",
                    "#......................#",
                    "#......................#",
                    "#....c...........c.....#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "#......................#",
                    "########################",
                }
            };
            fuera.Puertas.Add(new Puerta { Tile = new Point(0, 7), Destino = 1, Spawn = C(22, 7) });
            fuera.Luces.Add((C(17, 9), 80f));   // resplandor de una farola
            fuera.Luces.Add((C(2, 7), 60f));    // luz de la entrada
            fuera.Npcs.Add(N("parado", C(8, 4), Bata, "¿También te han dejado fuera?", "Aquí ya no hay reglas."));
            fuera.Npcs.Add(N("sentado", C(16, 11), Gris, "La policía no viene. Nadie viene.", "Solo queda correr."));
            fuera.Npcs.Add(N("parado", C(4, 12), Bata, "Corre. No mires atrás.", "Él está en el quirófano."));
            fuera.Npcs.Add(N("dormido", C(19, 3), Sabana, "Está inconsciente. Respira con dificultad."));
            fuera.Npcs.Add(N("parado", C(12, 3), Gris, "Nadie sale del hospital a estas horas...", "¿Trabajas ahí dentro?"));
            fuera.Npcs.Add(N("parado", C(21, 8), Bata, "Vi luces raras en el sótano.", "No entres sola."));
            fuera.Npcs.Add(N("sentado", C(8, 9), Gris, "Estoy esperando a alguien...", "Llevo horas esperando."));
            fuera.Npcs.Add(N("parado", C(13, 13), Verde, "¿Tienes un cigarrillo?", "Qué noche tan larga."));

            var lista = new List<Cuarto> { hab, pas, quir, farm, morg, fuera };

            // Sangre vieja solo en la morgue (4) y en el quirófano del caníbal (2).
            // En el resto del hospital la sangre aparece únicamente por lo que hagas.
            foreach (int i in new[] { 4, 2 })
            {
                var r = new Random(i * 11 + 3);
                while (lista[i].Manchas.Count < 6)
                {
                    int tx = r.Next(W), ty = r.Next(H);
                    if (!lista[i].Solido(tx, ty))
                        lista[i].Manchas.Add(new RectangleF(tx * T + r.Next(0, 20), ty * T + r.Next(0, 20), r.Next(10, 22), r.Next(8, 18)));
                }
            }
            foreach (Cuarto c in lista) c.ManchasFijas = c.Manchas.Count;
            foreach (Cuarto c in lista)
                switch (c.Nombre)
                {
                    case "Habitación 12": c.Tinte = Color.FromArgb(26, 40, 120, 110); break;
                    case "Pasillo central": c.Tinte = Color.FromArgb(24, 100, 130, 60); break;
                    case "Quirófano": c.Tinte = Color.FromArgb(28, 50, 140, 120); break;
                    case "Farmacia": c.Tinte = Color.FromArgb(22, 120, 120, 50); break;
                    case "Morgue": c.Tinte = Color.FromArgb(34, 40, 80, 170); break;
                    case "Afuera del hospital": c.Tinte = Color.FromArgb(40, 30, 50, 130); break;
                }
            return lista;
        }

        void Reiniciar()
        {
            cuartos = Construir();
            actual = 0;
            px = 11 * T + T / 2f; py = 7 * T + T / 2f;
            angulo = 0;

            canibalRef = null; cuartoCanibal = 0;
            for (int i = 0; i < cuartos.Count; i++)
                foreach (Npc n in cuartos[i].Npcs)
                    if (n.Tipo == "destrozado") { canibalRef = n; cuartoCanibal = i; }
            hambriento = false; canibalLibre = false; avisoHambre = false;
            hambre = 0; canibalEspera = 0; canibalComiendo = 0; latidoCanibal = 0; cercaniaCanibal = 0;
            entrada = new PointF(px, py);
            tituloPerder = "TE DETUVO LA POLICÍA";

            stamina = 100; cansado = false; linterna = true; mano = null; armaSangrienta = false;
            objetivo = null; prompt = ""; alertados = 0; partesServidas = 0;
            llaveSoltada = false; tiempoSpawn = 25; policiaTimer = 0;
            policiaLlamada = false; policiaLlego = false;

            // Cordura y mejoras de las cartas
            cordura = CorduraMax; enloquecido = false; ataqueLocura = 0; tiempoPastillas = 18;
            staminaMax = 100; velJugador = 120; extraTiempoHambre = 0; confianzaExtra = 0;
            silencioAlCortar = false; canibalLento = false; evitarAlerta50 = false; masPastillas = false;
            eligiendoCarta = false; cartasActuales.Clear();
            stamina = staminaMax;

            // Alucinaciones y dones nuevos
            alucinaciones.Clear(); tiempoAlucinacion = 6f; calmaPastillas = 0f; yaAvisoAlucinacion = false;
            calmaMental = 0; segundoAliento = 0;
            ojoDelSer = false; sangreFria = false; corteLimpio = false; estomagoNegro = false;
            pasoFantasma = false; pastillasFuertes = false; veloMental = false;

            hablando = null; dialogo.Clear(); dialogoIdx = 0;
            motivoPerder = "La policía te detuvo.";
            poblacionInicial = new int[cuartos.Count];
            for (int i = 0; i < cuartos.Count; i++) poblacionInicial[i] = Vivos(cuartos[i]);

            // Hasta que no te encuentres con el caníbal, el hospital parece desierto.
            conocioCanibal = false;
            npcsOcultos.Clear();
            OcultarNpcsHastaCanibal();

            parpadeo = 0; sacudida = 0; eventoAmbiente = 12;
            fade = 0; faseFade = 0; tiempoPartida = 0; tiempoNombre = 3;
            Mensaje("Despiertas en la habitación 12. La puerta está atada con vendas: busca algo para cortarlas.", 7);
        }

        // Guarda a todos los pacientes y visitantes fuera de escena hasta conocer al caníbal
        void OcultarNpcsHastaCanibal()
        {
            for (int i = 0; i < cuartos.Count; i++)
            {
                Cuarto c = cuartos[i];
                for (int j = c.Npcs.Count - 1; j >= 0; j--)
                {
                    Npc n = c.Npcs[j];
                    if (n.Tipo == "destrozado" || n.Tipo == "comido") continue;   // el caníbal y su víctima siguen ahí
                    npcsOcultos.Add((i, n));
                    c.Npcs.RemoveAt(j);
                }
            }
        }

        void ConocerCanibal()
        {
            if (conocioCanibal) return;
            conocioCanibal = true;

            foreach (var (ci, npc) in npcsOcultos)
                cuartos[ci].Npcs.Add(npc);
            npcsOcultos.Clear();

            Mensaje("Algo enorme respira en la oscuridad... La gente del hospital empieza a moverse.", 6);
            Rugido(true);
        }

        // ================= LÓGICA =================
        void Actualizar(float dt)
        {
            tiempo += dt;
            if (estado != Estado.Jugando) return;

            // Cambio de habitación: fundido a negro, cambio de escena y vuelta de la luz
            if (faseFade == 1)
            {
                fade += dt / 0.3f;
                if (fade >= 1) { fade = 1; CambiarCuarto(puertaUsada); faseFade = 2; }
                return;
            }
            if (faseFade == 2)
            {
                fade -= dt / 0.3f;
                if (fade <= 0) { fade = 0; faseFade = 0; }
                return;
            }

            tiempoPartida += dt;

            // Mientras hablas con alguien, el mundo se queda quieto
            if (hablando != null) return;
            if (eligiendoCarta) return;

            // --- Movimiento de la enfermera ---
            float dx = 0, dy = 0;
            if (Pulsada(Keys.W) || Pulsada(Keys.Up)) dy -= 1;
            if (Pulsada(Keys.S) || Pulsada(Keys.Down)) dy += 1;
            if (Pulsada(Keys.A) || Pulsada(Keys.Left)) dx -= 1;
            if (Pulsada(Keys.D) || Pulsada(Keys.Right)) dx += 1;
            bool moviendo = dx != 0 || dy != 0;
            if (moviendo) { float l = (float)Math.Sqrt(dx * dx + dy * dy); dx /= l; dy /= l; }

            corriendo = moviendo && Pulsada(Keys.ShiftKey) && !cansado && stamina > 0;
            if (corriendo) { stamina -= 35 * dt; if (stamina <= 0) { stamina = 0; cansado = true; } }
            else { stamina = Math.Min(staminaMax, stamina + 20 * dt); if (stamina > staminaMax * 0.25f) cansado = false; }

            float vel = corriendo ? velJugador * 1.65f : velJugador;
            Mover(ref px, ref py, dx * vel * dt, dy * vel * dt, 10);

            // La enfermera (y su linterna) miran hacia donde camina
            if (moviendo)
                angulo = GirarHacia(angulo, (float)Math.Atan2(dy, dx), 11f * dt);

            BuscarInteraccion();

            // Si te ven con el arma manchada de sangre, huyen aunque no les hayas cortado
            if (armaSangrienta)
            {
                int nuevos = 0;
                foreach (Npc n in cuarto.Npcs)
                {
                    if (n.Cortado || n.Huye || n.Alerta || n.Sigue || n.Tipo == "dormido" ||
                        n.Tipo == "destrozado" || n.Tipo == "comido" || n.Tipo == "policia") continue;
                    if (Dist(px, py, n.Pos.X, n.Pos.Y) > 260) continue;
                    if (!VisionLibre(n.Pos.X, n.Pos.Y, px, py)) continue;
                    Asustar(n);
                    nuevos++;
                }
                if (nuevos > 0) GritoPanico();
            }

            ActualizarNpcs(dt);
            ActualizarHambre(dt);
            ActualizarCordura(dt);
            ActualizarAlucinaciones(dt);
            ActualizarPolvo(dt);

            LimpiarConElTiempo(dt);

            // Van apareciendo pastillas de cordura por la habitación
            tiempoPastillas -= dt;
            if (tiempoPastillas <= 0)
            {
                tiempoPastillas = IntervaloPastillas();
                IntentarSpawnPastilla();
            }

            // La gente que falta va reapareciendo con el tiempo
            tiempoSpawn -= dt;
            if (tiempoSpawn <= 0)
            {
                tiempoSpawn = 18 + (float)rnd.NextDouble() * 14;
                IntentarSpawnNpc();
            }

            // Con 10 alertas alguien llama a la policía: llega unos segundos después y te persigue para detenerte
            if (alertados >= AlertasMax)
            {
                if (!policiaLlamada)
                {
                    policiaLlamada = true;
                    policiaTimer = 10;
                    Sirena();
                    Mensaje("¡Alguien llamó a la policía! Llegarán en unos segundos...", 5);
                }
                policiaTimer -= dt;
                if (policiaTimer <= 0)
                {
                    policiaTimer = 8;
                    IntentarPolicia();
                }
            }

            // --- Efectos de terror ---
            if (parpadeo > 0) parpadeo -= dt;
            else if (rnd.NextDouble() < 0.004) parpadeo = 0.05f + (float)rnd.NextDouble() * 0.15f;

            eventoAmbiente -= dt;
            if (eventoAmbiente <= 0)
            {
                eventoAmbiente = 10 + (float)rnd.NextDouble() * 15;
                Sonido(rnd.Next(200, 400), 150);
                parpadeo = 0.4f;
            }

            if (sacudida > 0) sacudida -= dt;
            if (tiempoMensaje > 0) tiempoMensaje -= dt;
            if (tiempoNombre > 0) tiempoNombre -= dt;
        }

        // Busca lo más cercano con lo que se puede interactuar (E)
        void BuscarInteraccion()
        {
            objetivo = null; prompt = "";
            float mejor = 64;

            foreach (Objeto o in cuarto.Objetos)
            {
                float d = Dist(px, py, o.Pos.X, o.Pos.Y);
                if (d < mejor) { mejor = d; objetivo = o; prompt = $"[E] Recoger: {o.Nombre}"; }
            }
            foreach (Cortable c in cuarto.Cortables)
            {
                if (c.Cortado) continue;
                float d = Dist(px, py, c.Pos.X, c.Pos.Y);
                if (d < mejor) { mejor = d; objetivo = c; prompt = $"[E] Cortar {c.Nombre}"; }
            }
            foreach (Puerta p in cuarto.Puertas)
            {
                if (p.Atada != null && !p.Atada.Cortado) continue;   // atada: primero hay que cortar
                PointF centro = C(p.Tile.X, p.Tile.Y);
                float d = Dist(px, py, centro.X, centro.Y);
                if (d < mejor) { mejor = d; objetivo = p; prompt = p.Destino == 5 ? "[E] Salir del hospital" : "[E] Abrir puerta"; }
            }
            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Cortado || n.Tipo == "comido") continue;
                float d = Dist(px, py, n.Pos.X, n.Pos.Y);
                if (n.Tipo == "destrozado") d -= (EscalaCanibal() - 1f) * 24f;   // al ser enorme, se le alcanza desde más lejos
                if (d < mejor)
                {
                    mejor = d; objetivo = n;
                    if (n.Tipo == "destrozado")
                        prompt = EsParte(mano) || ParteCerca(n) != null ? "[E] Darle de comer al caníbal" : "[E] Hablar";
                    else if (n.Tipo == "policia")
                        prompt = "[E] Hablar con el policía";
                    else if (n.Sigue)
                        prompt = TieneFilo ? "[E] Opciones (te sigue)   [X] Cortar" : "[E] Opciones (te sigue)";
                    else
                        prompt = TieneFilo ? "[E] Opciones   [X] Cortar extremidad" : "[E] Opciones: hablar / atraer";
                }
            }
        }

        bool TieneFilo => mano == "Tijeras" || mano == "Bisturí" || mano == "Cuchillo de enfermería";
        static bool EsParte(string s) => s == "Brazo amputado" || s == "Pierna amputada";

        // Tecla E
        void Interactuar()
        {
            if (estado != Estado.Jugando || faseFade != 0 || objetivo == null) return;

            switch (objetivo)
            {
                case Objeto o:
                    if (o.Nombre == "Pastillas de cordura")
                    {
                        cuarto.Objetos.Remove(o);
                        TomarPastillas();
                        break;
                    }
                    if (mano != null) { Mensaje("Ya tienes algo en la mano. Suéltalo con Q.", 2.5f); break; }
                    mano = o.Nombre;
                    armaSangrienta = false;
                    cuarto.Objetos.Remove(o);
                    Sonido(500, 60);
                    Mensaje($"Recoges: {mano}", 2);
                    break;

                case Cortable c:
                    if (mano == null) Mensaje("Necesito algo afilado para cortar esto.", 3);
                    else if (mano == c.Herramienta || (c.Herramienta == "cualquiera" && TieneFilo))
                    {
                        c.Cortado = true;
                        sacudida = 0.2f;
                        Sonido(250, 120);
                        if (c.Revela != null) cuarto.Objetos.Add(new Objeto(c.Revela, c.Pos));
                        Mensaje(c.Texto, 4);
                    }
                    else Mensaje($"Con {mano} no puedo cortar esto.", 3);
                    break;

                case Puerta p:
                    UsarPuerta(p);
                    break;

                case Npc n:
                    if (n.Tipo == "destrozado") Alimentar(n);
                    else if (n.Tipo == "comido") Mensaje("Es un cuerpo a medio comer. No quiero tocarlo.", 3);
                    else if (n.Tipo == "policia") Mensaje("El policía no quiere hablar. ¡Aléjate!", 3);
                    else if (n.Cortado) Mensaje("Ya no respira.", 3);
                    else if (n.Huye) Mensaje("¡No te acerques! ¡Te vi hacerlo!", 3);
                    else AbrirMenu(n);
                    break;
            }
        }

        // Tecla X: usar el filo que llevas en la mano sobre quien tengas delante
        void UsarFilo()
        {
            if (estado != Estado.Jugando || faseFade != 0) return;

            if (!TieneFilo)
            {
                Mensaje(mano == null ? "No llevo nada con filo en la mano." : $"Con {mano} no puedo cortar a nadie.", 2.5f);
                return;
            }

            Npc cerca = null;
            float mejor = 70;
            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Cortado || n.Tipo == "destrozado" || n.Tipo == "policia" || n.Tipo == "comido") continue;
                float d = Dist(px, py, n.Pos.X, n.Pos.Y);
                if (d < mejor) { mejor = d; cerca = n; }
            }
            if (cerca == null) { Mensaje("No hay nadie lo bastante cerca.", 2); return; }

            CortarA(cerca);
        }

        // Arranca una extremidad: sangre, parte suelta y rastro
        void CortarA(Npc cerca)
        {
            if (!TieneFilo) return;
            cerca.Sigue = false;   // si te seguía, deja de hacerlo

            bool brazo = cerca.Extremidades > 2;
            string parte = brazo ? "Brazo amputado" : "Pierna amputada";
            cerca.Extremidades--;
            cerca.Sangrando = true;
            cerca.SangreTimer = 0;

            // El arma queda manchada de sangre
            armaSangrienta = true;

            // Si puede moverse, huye de ti y pierde velocidad con cada extremidad
            if (cerca.Tipo != "dormido") Huir(cerca);
            if (!silencioAlCortar) { GritoDolor(); cerca.Grito = 1.4f; }
            PerderCordura(corteLimpio ? 3.6f : 6f);

            ManchaSangre(cerca.Pos.X, cerca.Pos.Y + 4, 34, 18);
            float dx = cerca.Pos.X + rnd.Next(-14, 15), dy = cerca.Pos.Y + rnd.Next(4, 16);
            if (Solido(dx, dy)) { dx = cerca.Pos.X; dy = cerca.Pos.Y; }
            cuarto.Objetos.Add(new Objeto(parte, new PointF(dx, dy)));

            sacudida = 0.35f;
            Sonido(160, 220);

            // Los que lo vean salen corriendo
            int vistos = AlertarTestigos(cerca);
            string extra = vistos == 0 ? "" :
                           vistos == 1 ? " Alguien te ha visto u oído y huye." :
                           $" {vistos} personas te han visto u oído y huyen.";
            if (vistos > 0) extra += $" Alerta {alertados}/{AlertasMax}.";

            if (cerca.Extremidades <= 0)
            {
                cerca.Cortado = true;
                ManchaSangre(cerca.Pos.X, cerca.Pos.Y + 4, 48, 26);
                Mensaje($"Le arrancas {(brazo ? "un brazo" : "una pierna")}. Se desploma, sin extremidades.{extra}", 4);
            }
            else
            {
                Mensaje($"Le arrancas {(brazo ? "un brazo" : "una pierna")}. La sangre no deja de salir.{extra}", 4);
            }

        }

        // Alimentar al caníbal con las partes que arrancaste (en la mano o tiradas junto a él)
        void Alimentar(Npc ser)
        {
            Objeto delSuelo = EsParte(mano) ? null : ParteCerca(ser);
            if (!EsParte(mano) && delSuelo == null)
            {
                if (mano == null || TieneFilo) AbrirDialogoCanibal(ser);
                else Mensaje($"Con {mano} no puedo alimentarlo. Necesita brazos o piernas.", 3.5f);
                return;
            }

            string parte = EsParte(mano) ? mano : delSuelo.Nombre;
            if (EsParte(mano)) { mano = null; armaSangrienta = false; }
            else cuarto.Objetos.Remove(delSuelo);

            bool brazo = parte == "Brazo amputado";
            partesServidas++;
            PerderCordura(estomagoNegro ? 1.5f : 3f);
            sacudida = 0.4f + Math.Min(partesServidas, 20) * 0.03f;
            ManchaSangre(ser.Pos.X, ser.Pos.Y + 8, 30, 16);
            // cada parte deja más sangre repartida por la cámara
            ManchaSangre(ser.Pos.X + rnd.Next(-150, 151), ser.Pos.Y + rnd.Next(-90, 91), 24 + partesServidas, 14 + partesServidas / 2);
            Sonido(120, 260);
            GritoDolor();

            // Desde la 2.ª parte se intranquiliza: hay que seguir alimentándolo a tiempo o se levanta a cazar
            string aviso = "";
            if (partesServidas >= 2)
            {
                bool primera = !hambriento;
                hambriento = true;
                hambre = HambreMax() + extraTiempoHambre;
                avisoHambre = false;
                if (canibalLibre) { CalmarCanibal(ser); aviso = " El ser se calma... por ahora."; }
                else if (primera) aviso = " ¡El ser se intranquiliza! Tiene hambre: tráele más partes a tiempo o se levantará.";
            }

            // Con 10 partes o más escupe la llave del exterior (solo una vez). Después se le puede seguir dando comida
            if (partesServidas >= 10 && !llaveSoltada)
            {
                llaveSoltada = true;
                cuarto.Objetos.Add(new Objeto("Llave del exterior", new PointF(ser.Pos.X, ser.Pos.Y + 40 * EscalaCanibal())));
                Sonido(660, 250);
                Mensaje("El ser escupe una llave oxidada: «Sal... y tráeme más». Ya puedes salir al exterior." + aviso, 7);
                if (partesServidas % 5 == 0) AbrirCartas();
                return;
            }

            Mensaje($"Le das {(brazo ? "un brazo" : "una pierna")}. El ser traga y su aspecto empeora. Partes: {partesServidas}.{aviso}", 5);
            if (partesServidas % 5 == 0) AbrirCartas();
        }

        // Todos los que vean u oigan la escena se llevan la alerta y huyen.
        // Cada persona cuenta como una sola alerta (máximo 10)
        int AlertarTestigos(Npc victima)
        {
            int nuevos = 0;
            foreach (Npc t in cuarto.Npcs)
            {
                if (t == victima || t.Cortado || t.Tipo == "dormido" || t.Tipo == "destrozado" || t.Tipo == "comido" || t.Tipo == "policia" || t.Alerta) continue;
                float d = Dist(px, py, t.Pos.X, t.Pos.Y);
                if (d > 340) continue;

                bool loVe = VisionLibre(t.Pos.X, t.Pos.Y, px, py) ||
                            VisionLibre(t.Pos.X, t.Pos.Y, victima.Pos.X, victima.Pos.Y);
                bool loOye = !silencioAlCortar && d <= (pasoFantasma ? 110 : 230);   // el grito delata; silencio o paso fantasma lo evitan
                if (!loVe && !loOye) continue;

                // Carta "testigos ciegos": 50% de no alertar a cada persona
                if (evitarAlerta50 && rnd.NextDouble() < 0.5) continue;

                Asustar(t);
                nuevos++;
            }
            alertados = Math.Min(AlertasMax, alertados + nuevos);
            if (nuevos > 0) GritoPanico();
            return nuevos;
        }

        // Un NPC entra en pánico: se alerta y sale corriendo lejos de ti
        void Asustar(Npc n)
        {
            n.Alerta = true;
            n.Huye = true;
            n.Sigue = false;
            n.Espera = 0;
            n.Repath = 0;
            n.TieneDestino = false;
            ActualizarVelocidad(n);
        }

        // La víctima huye sin contar como testigo
        void Huir(Npc n)
        {
            n.Huye = true;
            n.Sigue = false;
            n.Espera = 0;
            n.Repath = 0;
            n.TieneDestino = false;
            ActualizarVelocidad(n);
        }

        void ActualizarVelocidad(Npc n)
        {
            if (n.Extremidades <= 0) { n.Vel = 0; return; }
            float baseV = n.Huye ? Math.Max(n.VelBase, 130f) : n.VelBase;
            n.Vel = baseV * (n.Extremidades / 4f);
        }

        void GritoDolor()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(950, 110);
                    Console.Beep(780, 140);
                    Console.Beep(520, 220);
                }
                catch { }
            });
        }

        void GritoPanico()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(1300, 80);
                    Console.Beep(1600, 90);
                    Console.Beep(1150, 150);
                }
                catch { }
            });
        }

        void ManchaSangre(float x, float y, float ancho, float alto)
        {
            cuarto.Manchas.Add(new RectangleF(
                x - ancho / 2 + rnd.Next(-4, 5),
                y - alto / 2 + rnd.Next(-3, 4),
                ancho, alto));
            if (cuarto.Manchas.Count > 220) cuarto.Manchas.RemoveAt(0);
        }

        void Perder(string motivo, string titulo = "TE DETUVO LA POLICÍA")
        {
            if (estado != Estado.Jugando) return;
            motivoPerder = motivo;
            tituloPerder = titulo;
            hablando = null;
            alucinaciones.Clear();
            estado = Estado.Descubierto;
            Sonido(900, 300);
            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 5; i++)
                    {
                        Console.Beep(700, 120);
                        Console.Beep(500, 120);
                    }
                }
                catch { }
            });
        }

        // Los pacientes y visitantes caminan sin rumbo por la habitación
        void ActualizarNpcs(float dt)
        {
            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Grito > 0f) n.Grito -= dt;
                if (n.Cortado || n.Tipo == "dormido" || n.Tipo == "destrozado") continue;
                n.Fase += dt;

                // Si sangra, va dejando un rastro rojo por donde camina
                if (n.Sangrando)
                {
                    n.SangreTimer -= dt;
                    if (n.SangreTimer <= 0)
                    {
                        n.SangreTimer = 0.26f;
                        ManchaSangre(n.Pos.X, n.Pos.Y + 6, 7, 5);
                    }
                }

                if (n.Tipo == "policia")   // la policía va directa a por ti
                {
                    IrHaciaJugador(n, dt, 0);
                    if (Dist(n.Pos.X, n.Pos.Y, px, py) < 26) { Perder("La policía te detuvo."); return; }
                    continue;
                }
                if (n.Sigue && !n.Huye) { IrHaciaJugador(n, dt, 52); continue; }   // el que atrajiste te sigue

                if (n.Espera > 0) { n.Espera -= dt; continue; }

                Point aqui = Tile(n.Pos.X, n.Pos.Y);

                if (n.Huye)
                {
                    // Huye en dirección contraria a la enfermera (o al caníbal si es de él de quien huye)
                    n.Repath -= dt;
                    if (n.Repath <= 0 || !n.TieneDestino || aqui == n.Destino)
                    {
                        n.Repath = 0.8f;
                        n.Destino = TileHuyendo(n.Pos, AmenazaDe(n));
                        n.Paso = SiguientePaso(aqui, n.Destino);
                        n.TieneDestino = true;
                        if (n.Paso == aqui) { n.Espera = 0.2f; continue; }
                    }
                }
                else if (!n.TieneDestino || aqui == n.Destino)
                {
                    n.Espera = 0.8f + (float)rnd.NextDouble() * 2.5f;
                    n.Destino = TileAleatorioCerca(n.Casa);
                    n.Paso = SiguientePaso(aqui, n.Destino);
                    n.TieneDestino = true;
                    continue;
                }

                PointF meta = C(n.Paso.X, n.Paso.Y);
                float dx = meta.X - n.Pos.X, dy = meta.Y - n.Pos.Y;
                float lon = (float)Math.Sqrt(dx * dx + dy * dy);

                if (lon < 2f)
                {
                    // Llegó al centro del tile: calcula el siguiente paso del camino
                    n.Paso = SiguientePaso(aqui, n.Destino);
                    if (n.Paso == aqui) { n.Espera = 0.6f; n.TieneDestino = false; }
                    continue;
                }

                float paso = Math.Min(n.Vel * dt, lon);
                float nx = n.Pos.X + dx / lon * paso;
                float ny = n.Pos.Y + dy / lon * paso;

                // No atravesar a la enfermera: se detiene y espera
                if (Dist(nx, ny, px, py) > 26)
                    n.Pos = new PointF(nx, ny);
                else
                    n.Espera = 0.4f;
            }
        }

        // ================= DIÁLOGOS CON LOS NPC =================
        void AbrirMenu(Npc n)
        {
            hablando = n;
            n.Espera = 0;
            n.TieneDestino = false;
            IniciarDialogo(n);
        }

        void IniciarDialogo(Npc n)
        {
            dialogo.Clear();
            dialogoIdx = 0;

            string[] frasesJugador = partesServidas >= 10 ? DialogosFinal
                                   : partesServidas >= 1 ? DialogosIntermedio
                                                         : DialogosNormal;
            int inicio = rnd.Next(frasesJugador.Length);

            // Conversación de ida y vuelta: la enfermera habla y el NPC responde
            dialogo.Add((true, frasesJugador[inicio % frasesJugador.Length]));
            if (n.Frases != null && n.Frases.Length > 0)
                dialogo.Add((false, n.Frases[n.Idx++ % n.Frases.Length]));
            dialogo.Add((true, frasesJugador[(inicio + 1) % frasesJugador.Length]));
            if (n.Frases != null && n.Frases.Length > 1)
                dialogo.Add((false, n.Frases[n.Idx++ % n.Frases.Length]));
            dialogo.Add((true, frasesJugador[(inicio + 2) % frasesJugador.Length]));
        }

        void AbrirDialogoCanibal(Npc ser)
        {
            hablando = ser;
            ser.Espera = 0;
            ser.TieneDestino = false;
            dialogo.Clear();
            dialogoIdx = 0;

            string[] frasesJugador = partesServidas >= 10 ? DialogosFinal
                                   : partesServidas >= 1 ? DialogosIntermedio
                                                         : DialogosNormal;
            string respuesta = ser.Frases != null && ser.Frases.Length > 0
                ? ser.Frases[rnd.Next(ser.Frases.Length)]
                : "Hambre...";

            dialogo.Add((true, frasesJugador[rnd.Next(frasesJugador.Length)]));
            dialogo.Add((false, respuesta));
            dialogo.Add((true, "Todavía no traigo carne. Espera..."));
            dialogo.Add((false, "Hambre... más carne..."));
        }

        string NombreNpc(Npc n)
        {
            if (n.Tipo == "policia") return "Policía";
            if (n.Tipo == "destrozado") return "El ser";
            if (n.Tipo == "comido") return "Cuerpo";
            if (n.Tipo == "dormido" || n.Bata == Sabana || n.Bata == SabanaMorgue) return "Paciente";
            if (n.Bata == Gris) return "Visitante";
            if (n.Bata == Verde) return "Médico";
            if (n.Bata == Bata) return "Paciente";
            return "Enfermero";
        }

        void AvanzarDialogo()
        {
            if (hablando == null) return;
            if (dialogoIdx >= dialogo.Count - 1) { hablando = null; return; }
            dialogoIdx++;
        }

        // Añade una réplica inmediata (por ejemplo, cuando falta un objeto para una opción)
        void DecirLinea(bool esJugador, string texto)
        {
            if (hablando == null) return;
            if (dialogo.Count == 0) { dialogo.Add((esJugador, texto)); dialogoIdx = 0; return; }
            int pos = Math.Min(dialogoIdx + 1, dialogo.Count);
            dialogo.Insert(pos, (esJugador, texto));
            dialogoIdx = pos;
        }

        void ManejarTeclaMenu(Keys k)
        {
            bool soloConversacion = hablando != null && hablando.Tipo == "destrozado";

            if (!soloConversacion)
            {
                if (k == Keys.D1 || k == Keys.NumPad1) { ElegirOpcion(1); return; }
                if (k == Keys.D2 || k == Keys.NumPad2) { ElegirOpcion(2); return; }
                if (k == Keys.D3 || k == Keys.NumPad3) { ElegirOpcion(3); return; }
                if (k == Keys.D4 || k == Keys.NumPad4) { ElegirOpcion(4); return; }
            }

            if (k == Keys.E || k == Keys.Enter || k == Keys.Space) AvanzarDialogo();
            else if (k == Keys.Q || k == Keys.Escape) hablando = null;
        }

        // 1 hablar, 2 atraerlo (o que se quede), 3 ofrecerle pastillas, 4 cortarle una extremidad
        void ElegirOpcion(int op)
        {
            Npc n = hablando;
            if (n == null || n.Tipo == "destrozado") return;

            switch (op)
            {
                case 1:
                    IniciarDialogo(n);
                    break;

                case 2:
                    if (n.Sigue)
                    {
                        n.Sigue = false; n.Espera = 0.5f; n.TieneDestino = false;
                        hablando = null;
                        Mensaje("Le dices que se quede aquí. Se detiene y espera.", 3.5f);
                    }
                    else if (Atraer(n, false)) hablando = null;
                    break;

                case 3:
                    if (n.Sigue || n.Tipo == "dormido") break;
                    if (mano != "Frasco de pastillas") { DecirLinea(true, "(Necesito el frasco de pastillas en la mano.)"); break; }
                    if (Atraer(n, true)) hablando = null;
                    break;

                case 4:
                    if (!TieneFilo) { DecirLinea(true, "(Necesito tijeras o un bisturí en la mano.)"); break; }
                    hablando = null;
                    CortarA(n);
                    break;
            }
        }

        // Intenta que el NPC te siga. Con pastillas es seguro; sin ellas puede desconfiar, y si insistes se asusta (una alerta más)
        bool Atraer(Npc n, bool seguro)
        {
            if (n.Alerta || n.Huye) { DecirLinea(false, "¡Aléjate de mí!"); return false; }

            // Confiar cuesta: sin el don del ser, solo 3 de cada 10 intentos funcionan
            if (!seguro && rnd.NextDouble() > Math.Min(0.95, 0.30 + confianzaExtra * 0.12))
            {
                n.Confiabilidad--;
                if (n.Confiabilidad < -1 - confianzaExtra)
                {
                    Asustar(n);
                    alertados = Math.Min(AlertasMax, alertados + 1);
                    GritoPanico();
                    hablando = null;
                    Mensaje("Se asusta por tu insistencia y echa a correr. ¡Una alerta más!", 4);
                    return false;
                }
                DecirLinea(false, "No... no me fío de ti. Déjame en paz.");
                return false;
            }

            if (n.Tipo == "dormido") Despertar(n);
            n.Sigue = true; n.TieneDestino = false; n.Espera = 0; n.Repath = 0;
            n.VelBase = 95;
            ActualizarVelocidad(n);
            Mensaje(seguro ? "Ve las pastillas y te sigue. Llévalo a otra habitación."
                           : "Acepta y te sigue. Llévalo a otra habitación.", 4);
            return true;
        }

        // Despierta a un paciente dormido: se levanta de la cama y queda de pie a su lado
        void Despertar(Npc n)
        {
            n.Tipo = "parado";
            PointF libre = PosLibreCerca(n.Pos, null);
            n.Pos = libre;
            n.Casa = libre;
        }

        // Tile libre más cercano a un punto (para colocar gente sin meterla en una pared o en una cama)
        PointF PosLibreCerca(PointF centro, HashSet<Point> usados, float distMinima = 30f)
        {
            Point c = Tile(centro.X, centro.Y);
            for (int r = 0; r <= 5; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        var t = new Point(c.X + dx, c.Y + dy);
                        if (cuarto.Solido(t.X, t.Y) || (usados != null && usados.Contains(t))) continue;
                        PointF p = C(t.X, t.Y);
                        if (Dist(p.X, p.Y, px, py) < distMinima) continue;
                        if (usados != null) usados.Add(t);
                        return p;
                    }
            return centro;
        }

        // Camina por el camino más corto hacia la enfermera (acompañantes y policías)
        void IrHaciaJugador(Npc n, float dt, float distanciaMinima)
        {
            if (Dist(n.Pos.X, n.Pos.Y, px, py) <= distanciaMinima) return;

            n.Repath -= dt;
            if (n.Repath <= 0)
            {
                n.Paso = SiguientePaso(Tile(n.Pos.X, n.Pos.Y), Tile(px, py));
                n.Repath = 0.25f;
            }

            PointF meta = C(n.Paso.X, n.Paso.Y);
            float dx = meta.X - n.Pos.X, dy = meta.Y - n.Pos.Y;
            float lon = (float)Math.Sqrt(dx * dx + dy * dy);
            if (lon > 1)
            {
                float paso = Math.Min(n.Vel * dt, lon);
                n.Pos = new PointF(n.Pos.X + dx / lon * paso, n.Pos.Y + dy / lon * paso);
            }
        }

        // ================= HAMBRE Y CAZA DEL CANÍBAL =================
        // Cuánto tiempo aguanta sin comer: cuanto más ha comido, menos paciencia tiene
        float HambreMax() => Math.Max(35f, 70f - partesServidas * 3f);

        // De quién huye un NPC asustado: del caníbal (si él lo asustó) o de la enfermera
        PointF AmenazaDe(Npc n) =>
            n.HuyeDelCanibal && canibalRef != null && cuartoCanibal == actual ? canibalRef.Pos : new PointF(px, py);

        void ActualizarHambre(float dt)
        {
            if (canibalRef == null) return;
            cercaniaCanibal = 0;

            if (canibalLibre) { ActualizarCanibalLibre(dt); return; }
            if (!hambriento) return;

            hambre -= dt;
            if (hambre <= 12f && !avisoHambre)
            {
                avisoHambre = true;
                Mensaje("El ser gruñe y se retuerce... ¡tráele una parte ya!", 4);
                Rugido(false);
            }
            if (hambre <= 0) LiberarCanibal();
        }

        // El tiempo se acabó: se levanta, mata a la gente y te busca
        void LiberarCanibal()
        {
            canibalLibre = true; canibalEspera = 0; canibalComiendo = 0;
            canibalRef.Casa = canibalRef.Pos;
            sacudida = 0.9f;
            Rugido(true);
            Mensaje("¡El ser se levanta! Está libre y busca carne... ¡CORRE!", 6);
            if (cuartoCanibal == actual) PanicoPorCanibal(null);   // quien lo vea levantarse echa a correr
        }

        void CalmarCanibal(Npc ser)
        {
            canibalLibre = false; canibalEspera = 0; canibalComiendo = 0;
            ser.Casa = ser.Pos;
        }

        // ================= CORDURA, LOCURA Y PASTILLAS =================
        // Cuantas más partes le hayas dado al ser, más rápido se te va la cabeza
        float FactorLocura() => 1f + partesServidas * 0.12f;

        void PerderCordura(float cantidad)
        {
            if (cantidad <= 0) return;
            cordura = Math.Max(0f, cordura - cantidad * FactorLocura());
            if (cordura <= 0f) ColapsoMental();
        }

        // La cordura llegó a 0: el don "Segundo aliento" puede salvarte una vez
        void ColapsoMental()
        {
            if (enloquecido) return;
            if (segundoAliento > 0)
            {
                segundoAliento--;
                cordura = 45f;
                ataqueLocura = 1.5f;
                Mensaje("Tu mente aguanta... por ahora. (Segundo aliento)", 4);
                Sonido(560, 220);
                return;
            }
            VolverseLoco();
        }

        void TomarPastillas()
        {
            float cura = pastillasFuertes ? 60f : 35f;
            cordura = Math.Min(CorduraMax, cordura + cura);
            if (cordura > 0f) enloquecido = false;
            ataqueLocura = 0;

            // Don "Velo mental": las pastillas espantan las alucinaciones un rato
            if (veloMental) calmaPastillas = Math.Max(calmaPastillas, 12f);

            Sonido(700, 80);
            Mensaje($"Tomas pastillas de cordura (+{(int)cura}). Cordura: {(int)cordura}%", 3);
        }

        void VolverseLoco()
        {
            if (enloquecido) return;
            enloquecido = true;
            ataqueLocura = 1.5f;
            sacudida = 0.6f;
            GritoDolor();
            Mensaje("HAS PERDIDO LA CORDURA... las figuras vienen a por ti. Busca pastillas azules.", 6);
        }

        void ActualizarCordura(float dt)
        {
            if (cordura > 0f)
            {
                float factor = FactorLocura();
                cordura -= 0.35f * factor * dt;   // desgaste natural de la noche
                if (cercaniaCanibal > 0.05f) cordura -= cercaniaCanibal * 1.5f * factor * dt;
                if (cordura <= 0f) { cordura = 0f; ColapsoMental(); }
            }

            if (!enloquecido) return;

            // Enloquecido: ataca lentamente a quien tenga más cerca, sin poder evitarlo.
            // Con "Sangre fría" tarda mucho más entre ataque y ataque.
            ataqueLocura -= dt;
            if (ataqueLocura > 0f) return;
            ataqueLocura = sangreFria ? 5.5f : 2.8f;

            Npc presa = null;
            float mejor = 95f;
            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Cortado || n.Tipo == "comido" || n.Tipo == "destrozado") continue;
                float d = Dist(px, py, n.Pos.X, n.Pos.Y);
                if (d < mejor) { mejor = d; presa = n; }
            }
            if (presa != null) AtaqueLocura(presa);
        }

        void AtaqueLocura(Npc n)
        {
            n.Sigue = false;
            bool brazo = n.Extremidades > 2;
            n.Extremidades = Math.Max(0, n.Extremidades - 1);
            n.Sangrando = true; n.SangreTimer = 0;
            if (n.Tipo != "dormido") Huir(n);

            ManchaSangre(n.Pos.X, n.Pos.Y + 4, 34, 18);
            sacudida = 0.35f;
            if (!silencioAlCortar) { GritoDolor(); n.Grito = 1.4f; }

            int vistos = AlertarTestigos(n);
            string extra = vistos > 0 ? $" Alerta {alertados}/{AlertasMax}." : "";

            if (n.Extremidades <= 0)
            {
                n.Cortado = true;
                ManchaSangre(n.Pos.X, n.Pos.Y + 4, 48, 26);
                Mensaje($"La locura te domina: matas a alguien con las manos desnudas.{extra}", 4);
            }
            else
            {
                Mensaje($"La locura te domina: le arrancas {(brazo ? "un brazo" : "una pierna")} sin poder evitarlo.{extra}", 4);
            }
        }

        // ================= ALUCINACIONES DE LA CORDURA CRÍTICA =================
        // Con la cordura por debajo de 30 aparecen figuras que solo tú ves.
        // Si alguna te toca, pierdes la partida.
        const float CorduraCritica = 30f;

        float VelocidadAlucinacion()
        {
            float v = 50f + (CorduraCritica - cordura) * 1.3f;   // más rápidas cuanto más baja la cordura
            if (enloquecido) v *= 1.2f;
            if (calmaMental > 0) v *= Math.Max(0.55f, 1f - calmaMental * 0.2f);
            return v;
        }

        float IntervaloAlucinacion()
        {
            float t = (enloquecido ? 4.5f : 7f) - (CorduraCritica - cordura) * 0.12f;
            t *= 1f + calmaMental * 0.55f;
            if (enloquecido) t *= 0.75f;
            return Math.Max(2.2f, t) + (float)rnd.NextDouble() * 2f;
        }

        void SpawnAlucinacion()
        {
            bool encontrado = false;
            PointF p = new PointF(px + 240, py);
            for (int i = 0; i < 80; i++)
            {
                float x = rnd.Next(60, W * T - 60), y = rnd.Next(60, H * T - 60);
                if (Solido(x, y) || Dist(x, y, px, py) < 220) continue;
                p = new PointF(x, y);
                encontrado = true;
                break;
            }
            if (!encontrado)
            {
                float a = (float)(rnd.NextDouble() * Math.PI * 2);
                p = new PointF(px + (float)Math.Cos(a) * 250, py + (float)Math.Sin(a) * 250);
            }

            alucinaciones.Add(new Alucinacion
            {
                Pos = p,
                Vida = 0f,
                Vel = VelocidadAlucinacion(),
                Fase = (float)rnd.NextDouble() * 6f,
                Susurro = 0.6f,
            });
            Sonido(110 + rnd.Next(0, 50), 300);
            sacudida = Math.Max(sacudida, 0.25f);
        }

        void ActualizarAlucinaciones(float dt)
        {
            if (calmaPastillas > 0f) calmaPastillas -= dt;

            bool critico = (cordura <= CorduraCritica || enloquecido) && calmaPastillas <= 0f;

            if (critico && !yaAvisoAlucinacion)
            {
                yaAvisoAlucinacion = true;
                Mensaje("Las paredes respiran... hay figuras que te miran. ¡NO DEJES QUE TE TOQUEN!", 5);
                Sonido(140, 500);
            }
            else if (!critico && yaAvisoAlucinacion)
            {
                yaAvisoAlucinacion = false;
                tiempoAlucinacion = 5f;   // margen antes de que vuelvan
            }

            // Si recuperas la cordura (o usas el Velo mental), se desvanecen
            if (!critico)
            {
                for (int i = alucinaciones.Count - 1; i >= 0; i--)
                {
                    alucinaciones[i].Vida -= dt * 2.5f;
                    if (alucinaciones[i].Vida <= 0f) alucinaciones.RemoveAt(i);
                }
                return;
            }

            // Aparecen poco a poco, con un máximo según lo mal que estés
            int max = (enloquecido ? 3 : 2) + (cordura <= 10f ? 1 : 0);
            if (calmaMental > 0) max = Math.Max(1, max - calmaMental);
            if (alucinaciones.Count >= max) tiempoAlucinacion = Math.Max(tiempoAlucinacion, 1.5f);

            tiempoAlucinacion -= dt;
            if (tiempoAlucinacion <= 0f && alucinaciones.Count < max)
            {
                tiempoAlucinacion = IntervaloAlucinacion();
                SpawnAlucinacion();
            }

            for (int i = alucinaciones.Count - 1; i >= 0; i--)
            {
                Alucinacion a = alucinaciones[i];
                a.Fase += dt;
                a.Vida = Math.Min(1f, a.Vida + dt * 2f);
                a.Vel = VelocidadAlucinacion();

                // Te persiguen flotando: atraviesan paredes, pero son más lentas que tú
                float dx = px - a.Pos.X, dy = py - a.Pos.Y;
                float lon = (float)Math.Sqrt(dx * dx + dy * dy);
                if (lon > 1f)
                {
                    float paso = Math.Min(a.Vel * dt, lon);
                    a.Pos = new PointF(
                        a.Pos.X + dx / lon * paso + (float)Math.Sin(tiempo * 9 + a.Fase) * 0.7f,
                        a.Pos.Y + dy / lon * paso + (float)Math.Cos(tiempo * 7 + a.Fase) * 0.5f);
                }

                a.Susurro -= dt;
                if (a.Susurro <= 0f && lon < 360f)
                {
                    a.Susurro = 3f + (float)rnd.NextDouble() * 4f;
                    Sonido(rnd.Next(90, 170), 220);
                }

                // Te tocó: la partida termina
                if (lon < 24f)
                {
                    alucinaciones.Clear();
                    Perder("Una figura te sujetó por el cuello. Nadie más la vio, pero tú sí notaste cómo te arrancaba la vida.",
                        "TUS ALUCINACIONES TE DEVORARON");
                    return;
                }
            }
        }

        // Sin el don, el intervalo entre pastillas crece con el tiempo; con el don se mantiene corto
        float IntervaloPastillas()
        {
            if (masPastillas)
                return 8f + Math.Min(20f, tiempoPartida * 0.10f) + (float)rnd.NextDouble() * 8f;
            return 20f + Math.Min(80f, tiempoPartida * 0.35f) + (float)rnd.NextDouble() * 10f;
        }

        // Aparece una pastilla de cordura en un tile libre de la habitación actual
        void IntentarSpawnPastilla()
        {
            // Sin el don del ser, cada vez escasean más
            int max = masPastillas ? 6 : Math.Max(1, 3 - (int)(tiempoPartida / 150f));
            int actuales = 0;
            foreach (Objeto o in cuarto.Objetos)
                if (o.Nombre == "Pastillas de cordura") actuales++;
            if (actuales >= max) return;

            for (int i = 0; i < 80; i++)
            {
                int tx = rnd.Next(1, W - 1), ty = rnd.Next(1, H - 1);
                if (cuarto.Solido(tx, ty)) continue;
                PointF p = C(tx, ty);
                if (Dist(px, py, p.X, p.Y) < 100) continue;
                cuarto.Objetos.Add(new Objeto("Pastillas de cordura", p));
                return;
            }
        }

        // ================= CARTAS DEL SER (cada 5 partes) =================
        static readonly string[] NombresCarta =
        {
            "MÁS ESTAMINA",
            "MÁS VELOCIDAD",
            "MÁS TIEMPO PARA EL SER",
            "SILENCIO AL CORTAR",
            "CONFIANZA",
            "CORDURA PLENA",
            "SER LENTO",
            "TESTIGOS CIEGOS",
            "MÁS PASTILLAS",
            "MEMORIA DE ACERO",
            "OJO DEL SER",
            "SANGRE FRÍA",
            "CORTE LIMPIO",
            "ESTÓMAGO NEGRO",
            "PASO FANTASMA",
            "FRASCO DOBLE",
            "SEGUNDO ALIENTO",
            "VELO MENTAL",
        };

        static readonly string[] DescrCarta =
        {
            "Tu estamina máxima sube 25 puntos y se llena al instante.",
            "Te mueves más rápido: +18 de velocidad base.",
            "Cada vez que alimentes al ser tendrá +20 s antes de volver a tener hambre.",
            "Los NPC no gritan ni alertan por el ruido cuando cortas extremidades.",
            "Los NPC confían más en ti y es más fácil que te sigan.",
            "Recuperas toda la cordura al instante y sales de la locura.",
            "Cuando el ser se libere por hambre, se moverá un 50% más lento.",
            "50% de probabilidad de que cada testigo no genere alerta al cortar.",
            "Frena la escasez: aparecerán pastillas de cordura mucho más a menudo.",
            "Las alucinaciones aparecen más tarde, más lentas y en menor número.",
            "Ves el brillo rojo de todas las personas a través de paredes y oscuridad.",
            "Cuando pierdes la cordura, atacas mucho más despacio.",
            "Cortar extremidades te quita un 40% menos de cordura.",
            "Alimentar al ser te quita la mitad de cordura.",
            "Los NPC solo te oyen si están muy cerca: su radio de escucha se reduce.",
            "Las pastillas restauran 60 de cordura en vez de 35.",
            "La próxima vez que tu cordura llegue a 0, recuperas 45 en vez de enloquecer.",
            "Al tomar pastillas, las alucinaciones desaparecen durante 12 segundos.",
        };

        void AbrirCartas()
        {
            var disponibles = new List<int> { 0, 1, 2, 4, 5 };   // estas se pueden repetir y acumular
            if (!silencioAlCortar) disponibles.Add(3);
            if (!canibalLento) disponibles.Add(6);
            if (!evitarAlerta50) disponibles.Add(7);
            if (!masPastillas) disponibles.Add(8);
            if (calmaMental < 2) disponibles.Add(9);
            if (!ojoDelSer) disponibles.Add(10);
            if (!sangreFria) disponibles.Add(11);
            if (!corteLimpio) disponibles.Add(12);
            if (!estomagoNegro) disponibles.Add(13);
            if (!pasoFantasma) disponibles.Add(14);
            if (!pastillasFuertes) disponibles.Add(15);
            if (segundoAliento <= 0) disponibles.Add(16);
            if (!veloMental) disponibles.Add(17);

            cartasActuales.Clear();
            for (int i = 0; i < 3 && disponibles.Count > 0; i++)
            {
                int k = rnd.Next(disponibles.Count);
                cartasActuales.Add(disponibles[k]);
                disponibles.RemoveAt(k);
            }

            eligiendoCarta = true;
            hablando = null;
            dialogo.Clear();
            Sonido(520, 120);
        }

        void ElegirCarta(int idx)
        {
            if (!eligiendoCarta || idx < 0 || idx >= cartasActuales.Count) return;

            switch (cartasActuales[idx])
            {
                case 0:
                    staminaMax += 25f;
                    stamina = staminaMax;
                    Mensaje("Carta elegida: más estamina. Tu resistencia máxima aumenta.", 4);
                    break;
                case 1:
                    velJugador += 18f;
                    Mensaje("Carta elegida: más velocidad. Te mueves más rápido.", 4);
                    break;
                case 2:
                    extraTiempoHambre += 20f;
                    if (hambriento && !canibalLibre) hambre += 20f;
                    Mensaje("Carta elegida: el ser tarda más en tener hambre tras cada comida.", 4);
                    break;
                case 3:
                    silencioAlCortar = true;
                    Mensaje("Carta elegida: cortas en silencio. Los NPC ya no te oyen gritar.", 4);
                    break;
                case 4:
                    confianzaExtra++;
                    Mensaje("Carta elegida: los NPC confían más en ti. Les cuesta menos seguirte.", 4);
                    break;
                case 5:
                    cordura = CorduraMax;
                    enloquecido = false;
                    ataqueLocura = 0;
                    Mensaje("Carta elegida: recuperas toda la cordura y vuelves a estar cuerda.", 4);
                    break;
                case 6:
                    canibalLento = true;
                    Mensaje("Carta elegida: cuando el ser se libere por hambre, será mucho más lento.", 4);
                    break;
                case 7:
                    evitarAlerta50 = true;
                    Mensaje("Carta elegida: la mitad de los testigos no darán alerta al cortar.", 4);
                    break;
                case 8:
                    masPastillas = true;
                    tiempoPastillas = Math.Min(tiempoPastillas, 4f);
                    IntentarSpawnPastilla();
                    Mensaje("Carta elegida: aparecerán muchas más pastillas de cordura.", 4);
                    break;
                case 9:
                    calmaMental++;
                    Mensaje("Carta elegida: tu memoria se endurece. Las alucinaciones tardan más y son más lentas.", 4);
                    break;
                case 10:
                    ojoDelSer = true;
                    Mensaje("Carta elegida: ves el brillo de todas las personas a través de las paredes.", 4);
                    break;
                case 11:
                    sangreFria = true;
                    Mensaje("Carta elegida: sangre fría. Cuando enloqueces, atacas mucho más despacio.", 4);
                    break;
                case 12:
                    corteLimpio = true;
                    Mensaje("Carta elegida: corte limpio. Cortar extremidades te cuesta menos cordura.", 4);
                    break;
                case 13:
                    estomagoNegro = true;
                    Mensaje("Carta elegida: estómago negro. Alimentar al ser te cuesta la mitad de cordura.", 4);
                    break;
                case 14:
                    pasoFantasma = true;
                    Mensaje("Carta elegida: paso fantasma. Los NPC casi no te oyen al cortar.", 4);
                    break;
                case 15:
                    pastillasFuertes = true;
                    Mensaje("Carta elegida: frasco doble. Cada pastilla restaura 60 de cordura.", 4);
                    break;
                case 16:
                    segundoAliento++;
                    Mensaje("Carta elegida: segundo aliento. La próxima caída a 0 no te hará enloquecer.", 4);
                    break;
                case 17:
                    veloMental = true;
                    calmaPastillas = Math.Max(calmaPastillas, 10f);
                    Mensaje("Carta elegida: velo mental. Las pastillas espantan las alucinaciones 12 s.", 4);
                    break;
            }

            cartasActuales.Clear();
            eligiendoCarta = false;
            Sonido(780, 140);
        }

        // Cambia al caníbal de habitación (cuando te sigue)
        void MoverCanibal(int destino, PointF cerca)
        {
            cuartos[cuartoCanibal].Npcs.Remove(canibalRef);
            cuartoCanibal = destino;
            PointF pos = destino == actual ? PosLibreCerca(cerca, null, 110f) : cerca;
            canibalRef.Pos = pos; canibalRef.Casa = pos; canibalRef.Repath = 0;
            cuartos[destino].Npcs.Add(canibalRef);
            canibalComiendo = 1.2f;   // se sacude un momento al entrar: te da unos segundos
        }

        void ActualizarCanibalLibre(float dt)
        {
            Npc ser = canibalRef;

            // Está en otra habitación: te sigue y aparece por la puerta por la que entraste
            if (cuartoCanibal != actual)
            {
                canibalEspera += dt;
                if (canibalEspera > 9f)
                {
                    MoverCanibal(actual, entrada);
                    canibalEspera = 0;
                    sacudida = 0.7f;
                    Rugido(true);
                    Mensaje("Algo enorme entró detrás de ti...", 3);
                }
                return;
            }
            canibalEspera = 0;

            float dJug = Dist(px, py, ser.Pos.X, ser.Pos.Y);
            cercaniaCanibal = Math.Max(0f, 1f - dJug / 380f);
            latidoCanibal -= dt;
            if (cercaniaCanibal > 0.05f && latidoCanibal <= 0) { Sonido(85, 90); latidoCanibal = 1.25f - cercaniaCanibal * 0.8f; }

            if (canibalComiendo > 0) { canibalComiendo -= dt; return; }

            // Va a por lo que tenga más cerca: una persona despierta o tú
            Npc presa = null;
            float mejor = dJug;
            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Cortado || n.Tipo == "dormido" || n.Tipo == "destrozado" || n.Tipo == "comido" || n.Tipo == "policia") continue;
                float d = Dist(n.Pos.X, n.Pos.Y, ser.Pos.X, ser.Pos.Y);
                if (d < mejor) { mejor = d; presa = n; }
            }
            PointF objetivoPos = presa != null ? presa.Pos : new PointF(px, py);

            float radioCaza = 18f * EscalaCanibal();
            if (presa != null && mejor < radioCaza + 8f) { MatarNpc(presa); return; }
            if (presa == null && dJug < radioCaza)
            {
                sacudida = 0.8f;
                Perder("El ser te atrapó entre sus garras y te devoró.", "EL CANÍBAL TE DEVORÓ");
                return;
            }

            // Camino más corto hasta el objetivo (y en línea recta cuando ya lo tiene a la vista)
            float vel = Math.Min(150f, 85f + partesServidas * 3f);
            if (canibalLento) vel *= 0.5f;   // carta: el ser se arrastra cuando se libera por hambre
            Point aqui = Tile(ser.Pos.X, ser.Pos.Y);
            ser.Repath -= dt;
            if (ser.Repath <= 0) { ser.Paso = SiguientePaso(aqui, Tile(objetivoPos.X, objetivoPos.Y)); ser.Repath = 0.2f; }

            float dirX, dirY;
            if (mejor < 160f && VisionLibre(ser.Pos.X, ser.Pos.Y, objetivoPos.X, objetivoPos.Y)) { dirX = objetivoPos.X - ser.Pos.X; dirY = objetivoPos.Y - ser.Pos.Y; }
            else { PointF c = C(ser.Paso.X, ser.Paso.Y); dirX = c.X - ser.Pos.X; dirY = c.Y - ser.Pos.Y; }

            float lon = (float)Math.Sqrt(dirX * dirX + dirY * dirY);
            if (lon > 1f)
            {
                float paso = Math.Min(vel * dt, lon);
                ser.Pos = new PointF(ser.Pos.X + dirX / lon * paso, ser.Pos.Y + dirY / lon * paso);
            }
        }

        // El caníbal mata a alguien: sangre, una parte suelta y pánico general
        void MatarNpc(Npc v)
        {
            v.Cortado = true; v.Extremidades = 0; v.Sangrando = false;
            v.Huye = false; v.Sigue = false; v.Vel = 0; v.TiempoMuerto = 0;
            ManchaSangre(v.Pos.X, v.Pos.Y + 4, 54, 30);
            ManchaSangre(v.Pos.X + rnd.Next(-30, 31), v.Pos.Y + rnd.Next(-20, 21), 26, 14);

            // deja una parte suelta (sirve para darle y calmarlo)
            float dx = v.Pos.X + rnd.Next(-16, 17), dy = v.Pos.Y + rnd.Next(6, 18);
            if (Solido(dx, dy)) { dx = v.Pos.X; dy = v.Pos.Y; }
            cuarto.Objetos.Add(new Objeto(rnd.Next(2) == 0 ? "Brazo amputado" : "Pierna amputada", new PointF(dx, dy)));

            canibalComiendo = 2.2f;
            sacudida = 0.5f;
            GritoDolor();
            v.Grito = 1.4f;
            PerderCordura(8f);

            int testigos = PanicoPorCanibal(v);
            Mensaje(testigos > 0 ? $"¡El ser devora a alguien! {testigos} testigo(s) lo vieron y huyen." : "¡El ser devora a alguien!", 4);
        }

        // Todos los que están cerca del caníbal entran en pánico y huyen de él.
        // Los que ven la muerte se alertan y cuentan para llamar a la policía
        int PanicoPorCanibal(Npc victima)
        {
            int nuevos = 0;
            Npc ser = canibalRef;
            foreach (Npc t in cuartos[cuartoCanibal].Npcs)
            {
                if (t == victima || t.Cortado || t.Tipo == "dormido" || t.Tipo == "destrozado" || t.Tipo == "comido" || t.Tipo == "policia") continue;
                if (Dist(t.Pos.X, t.Pos.Y, ser.Pos.X, ser.Pos.Y) > 700) continue;

                bool lovio = victima != null && Dist(t.Pos.X, t.Pos.Y, victima.Pos.X, victima.Pos.Y) < 420 &&
                             (VisionLibre(t.Pos.X, t.Pos.Y, victima.Pos.X, victima.Pos.Y) || VisionLibre(t.Pos.X, t.Pos.Y, ser.Pos.X, ser.Pos.Y));
                bool yaAlerta = t.Alerta;
                if (victima == null) Huir(t);   // solo por verlo levantarse: huye, pero aún no cuenta como alerta
                else Asustar(t);                // por ver una muerte: se alerta
                t.HuyeDelCanibal = true;
                if (lovio && !yaAlerta) nuevos++;
            }
            alertados = Math.Min(AlertasMax, alertados + nuevos);
            GritoPanico();
            return nuevos;
        }

        void Rugido(bool fuerte)
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(fuerte ? 55 : 90, fuerte ? 420 : 240);
                    Console.Beep(fuerte ? 70 : 110, fuerte ? 300 : 160);
                }
                catch { }
            });
        }

        void ActualizarPolvo(float dt)
        {
            foreach (float[] m in polvo)
            {
                m[0] += (m[2] + (float)Math.Sin(tiempo * 0.7 + m[4]) * 6f) * dt;
                m[1] += m[3] * dt;
                if (m[1] < -4) { m[1] = H * T + 4; m[0] = (float)rnd.NextDouble() * W * T; }
                if (m[0] < -4) m[0] = W * T + 4;
                if (m[0] > W * T + 4) m[0] = -4;
            }
        }

        // ================= LIMPIEZA CON EL TIEMPO =================
        // Las partes sueltas y los cuerpos desaparecen y la sangre vieja se seca, para que el juego no se sature
        void LimpiarConElTiempo(float dt)
        {
            foreach (Cuarto c in cuartos)
            {
                for (int i = c.Objetos.Count - 1; i >= 0; i--)
                {
                    if (!EsParte(c.Objetos[i].Nombre)) continue;
                    c.Objetos[i].Edad += dt;
                    if (c.Objetos[i].Edad > TiempoParte) c.Objetos.RemoveAt(i);
                }

                for (int i = c.Npcs.Count - 1; i >= 0; i--)
                {
                    Npc n = c.Npcs[i];
                    if (!n.Cortado || n.Tipo == "comido") continue;   // el cuerpo a medio comer del quirófano es decoración fija
                    n.TiempoMuerto += dt;
                    if (n.TiempoMuerto > TiempoCuerpo) c.Npcs.RemoveAt(i);
                }

                // Cada 4 segundos se seca la mancha más vieja (sin tocar las de decoración)
                c.Secado += dt;
                if (c.Secado > 4f)
                {
                    c.Secado = 0;
                    if (c.Manchas.Count > c.ManchasFijas) c.Manchas.RemoveAt(c.ManchasFijas);
                }
            }
        }

        // ================= POLICÍA Y REAPARICIÓN =================
        void Sirena()
        {
            Task.Run(() =>
            {
                try { for (int i = 0; i < 3; i++) { Console.Beep(900, 160); Console.Beep(650, 160); } }
                catch { }
            });
        }

        // La policía entra por la puerta más lejana de la habitación en la que estás
        void IntentarPolicia()
        {
            int total = 0;
            foreach (Cuarto c in cuartos)
                foreach (Npc n in c.Npcs)
                    if (n.Tipo == "policia") total++;
            if (total >= 4) return;

            PointF pos = default;
            float mejorDist = -1;
            foreach (Puerta pu in cuarto.Puertas)
            {
                int tx = pu.Tile.X == 0 ? 1 : pu.Tile.X == W - 1 ? W - 2 : pu.Tile.X;
                int ty = pu.Tile.Y == 0 ? 1 : pu.Tile.Y == H - 1 ? H - 2 : pu.Tile.Y;
                PointF p = C(tx, ty);
                float d = Dist(p.X, p.Y, px, py);
                if (d > mejorDist) { mejorDist = d; pos = p; }
            }

            if (mejorDist < 150)   // ninguna puerta lo bastante lejos: aparece en un tile libre alejado
            {
                for (int intento = 0; intento < 60 && mejorDist < 150; intento++)
                {
                    int tx = rnd.Next(1, W - 1), ty = rnd.Next(1, H - 1);
                    if (cuarto.Solido(tx, ty)) continue;
                    PointF p = C(tx, ty);
                    float d = Dist(p.X, p.Y, px, py);
                    if (d > mejorDist) { mejorDist = d; pos = p; }
                }
            }
            if (mejorDist < 0) return;

            Npc pol = N("policia", pos, Color.FromArgb(40, 70, 150), "¡Alto! ¡Policía!");
            pol.VelBase = 115; pol.Vel = 115; pol.Espera = 0;
            cuarto.Npcs.Add(pol);

            policiaLlego = true;
            Sirena();
            Mensaje("¡Llegó la policía! ¡No dejes que te alcancen!", 4);
        }

        // Gente "viva" de una habitación: sin contar al caníbal, los cuerpos ni la policía
        int Vivos(Cuarto c)
        {
            int k = 0;
            foreach (Npc n in c.Npcs)
                if (!n.Cortado && n.Tipo != "destrozado" && n.Tipo != "comido" && n.Tipo != "policia") k++;
            return k;
        }

        // Si a una habitación le falta gente respecto al principio, aparece alguien nuevo
        void IntentarSpawnNpc()
        {
            if (!conocioCanibal) return;   // el hospital está desierto hasta que encuentres al caníbal

            var faltan = new List<int>();
            for (int i = 0; i < cuartos.Count; i++)
                if (Vivos(cuartos[i]) < poblacionInicial[i]) faltan.Add(i);
            if (faltan.Count == 0) return;

            int ci = faltan[rnd.Next(faltan.Count)];
            Cuarto c = cuartos[ci];
            for (int intento = 0; intento < 60; intento++)
            {
                int tx = rnd.Next(1, W - 1), ty = rnd.Next(1, H - 1);
                if (c.Solido(tx, ty)) continue;
                PointF pos = C(tx, ty);
                // En tu habitación solo aparecen lejos de ti y fuera de tu vista
                if (ci == actual && (Dist(pos.X, pos.Y, px, py) < 300 || VisionLibre(pos.X, pos.Y, px, py))) continue;

                string[] frases = FrasesNuevos[rnd.Next(FrasesNuevos.Length)];
                c.Npcs.Add(N(rnd.Next(4) == 0 ? "sentado" : "parado", pos, BatasNpc[rnd.Next(BatasNpc.Length)], frases));
                return;
            }
        }

        // ================= CANÍBAL =================
        // Una parte tirada cerca del caníbal: él mismo la agarra
        Objeto ParteCerca(Npc ser)
        {
            foreach (Objeto o in cuarto.Objetos)
                if (EsParte(o.Nombre) && Dist(o.Pos.X, o.Pos.Y, ser.Pos.X, ser.Pos.Y) < 150) return o;
            return null;
        }

        string FraseCanibal()
        {
            if (partesServidas == 0) return "El ser tiembla y abre la boca. Necesita partes de cuerpos.";
            if (partesServidas < 5) return "«Más... más carne...» — el ser se relame.";
            if (partesServidas < 10) return "«Casi... casi libre... tráeme más.»";
            return "«Fuera hay mucha gente... tráeme más, enfermera.»";
        }

        // La luz de la cámara titila cada vez más a medida que el caníbal come
        // Tamaño del caníbal: crece con cada parte que come (hasta unas 3 veces su tamaño)
        float EscalaCanibal() => 1f + Math.Min(partesServidas, 25) * 0.08f;

        float LuzTitilante()
        {
            if (actual != 2) return 1f;
            return 1f - Math.Min(partesServidas, 20) * 0.012f * (0.5f + 0.5f * (float)Math.Sin(tiempo * 17));
        }

        // El caníbal se vuelve más grande y más grotesco con cada parte que se come
        void DibujarCanibal(Graphics g, Npc n)
        {
            int k = partesServidas;
            float esc = EscalaCanibal();
            bool furioso = canibalLibre || (hambriento && hambre < 12f);
            float agit = canibalLibre ? 2.6f : furioso ? 2f : 1f;
            float temblor = ((float)Math.Sin(tiempo * 27) * (1.9f + k * 0.08f) + (float)Math.Sin(tiempo * 43) * 1.1f) * agit;
            float pasoB = canibalLibre && canibalComiendo <= 0 ? (float)Math.Sin(tiempo * 9) * 2.5f * esc : 0;
            float x = n.Pos.X + temblor, y = n.Pos.Y + pasoB;

            // sombra enorme, charco de sangre y huesos esparcidos: crecen con cada parte
            DibujarSombra(g, n.Pos.X, n.Pos.Y + 24 * esc, 56 * esc);
            float rp = 24 + Math.Min(k, 25) * 3.5f;
            using (var charco = new SolidBrush(Color.FromArgb(200, 80, 6, 6)))
                g.FillEllipse(charco, n.Pos.X - rp, n.Pos.Y + 6 - rp * 0.45f, rp * 2, rp * 0.9f);
            g.FillEllipse(brSangreViva, n.Pos.X - rp * 0.6f, n.Pos.Y + 4 - rp * 0.25f, rp * 1.2f, rp * 0.5f);
            for (int i = 0; i < Math.Min(k, 16); i++)
            {
                double ang = i * 2.4;
                float r = (30 + (i % 4) * 11) * esc;
                float hx = n.Pos.X + (float)Math.Cos(ang) * r, hy = n.Pos.Y + 14 + (float)Math.Sin(ang) * r * 0.45f;
                g.DrawLine(penHueso, hx - 5, hy - 1, hx + 5, hy + 1 + (i % 2));
            }

            // aura roja cuando está furioso
            if (furioso)
            {
                int aa = (int)(50 + 35 * Math.Sin(tiempo * 7));
                using var aura = new SolidBrush(Color.FromArgb(Math.Max(10, aa), 255, 0, 0));
                float ra = 46 * esc;
                g.FillEllipse(aura, n.Pos.X - ra, n.Pos.Y - ra * 0.9f, ra * 2, ra * 1.8f);
            }

            GraphicsState guardado = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(esc, esc);
            using var bata = new SolidBrush(n.Bata);

            // harapos de la bata colgando
            g.FillPolygon(bata, new[] { new PointF(-15, 16), new PointF(-11, 30), new PointF(-6, 20), new PointF(-1, 32), new PointF(5, 21), new PointF(11, 30), new PointF(15, 16) });
            g.FillPolygon(brSombraSuave, new[] { new PointF(-1, 18), new PointF(-1, 32), new PointF(5, 21), new PointF(11, 30), new PointF(15, 16) });

            // brazos: se alargan, y a partir de 8 partes le salen otros dos
            float largo = Math.Min(k, 14);
            g.DrawLine(penBrazo, -11, 10, -24 - largo, 20 + largo * 0.6f);
            g.DrawLine(penBrazo, 11, 10, 24 + largo, 20 + largo * 0.6f);
            g.FillEllipse(Brushes.DarkRed, -29 - largo, 18 + largo * 0.6f, 8, 8);   // garras sucias
            g.FillEllipse(Brushes.DarkRed, 21 + largo, 18 + largo * 0.6f, 8, 8);
            if (k >= 8)
            {
                g.DrawLine(penBrazo, -9, 2, -30 - largo, -4);
                g.DrawLine(penBrazo, 9, 2, 30 + largo, -4);
            }

            g.FillEllipse(bata, -15, -8, 30, 30);                  // torso encogido
            g.FillEllipse(brSombraSuave, -3, -6, 18, 26);
            for (int i = 0; i < 3; i++) g.DrawLine(penHueso, -8, 3 + i * 5, 8, 5 + i * 5);   // costillas expuestas
            g.FillEllipse(brPiel, -10, -28, 20, 20);               // cabeza ladeada
            g.FillEllipse(brOjera, -10, -23, 20, 7);
            g.FillEllipse(brSangre, 1, -26, 11, 16);               // media cara destrozada
            g.DrawLine(penHilo, -2, -26, -2, -10);                 // costuras
            g.DrawLine(penHilo, -7, -18, 3, -18);

            // ojos: primero uno rojo, luego los dos con resplandor, y un tercero con 12 partes
            g.FillEllipse(k >= 3 ? Brushes.Red : Brushes.Black, -7, -22, 4, 5);
            g.FillEllipse(Brushes.Red, 5, -22, 4, 5);
            if (k >= 12) g.FillEllipse(Brushes.Red, -2, -28, 4, 4);
            if (k >= 6)
            {
                int ga = Math.Min(150, 40 + k * 8);
                using var brillo = new SolidBrush(Color.FromArgb(ga, 255, 0, 0));
                g.FillEllipse(brillo, -12, -26, 12, 12);
                g.FillEllipse(brillo, 2, -26, 12, 12);
            }

            // boca: se abre más (y más aún mientras devora), con dientes y baba de sangre
            float bw = 7 + Math.Min(k, 12) * 1.3f + (canibalComiendo > 0 ? 6 : 0);
            g.FillEllipse(Brushes.Black, -bw / 2, -13, bw, 5 + Math.Min(k, 12) * 0.5f);
            for (int i = 0; i < Math.Min(k / 2, 8); i++)
                g.FillRectangle(Brushes.White, -bw / 2 + 2 + i * (bw - 4) / 8f, -13, 1.6f, 3);
            if (k >= 4)
            {
                using var gota = new Pen(Color.FromArgb(210, 110, 10, 10), 3);
                g.DrawLine(gota, 0, -8, 0, 4 + k);
            }

            g.FillEllipse(brSangre, -5, 5, 16, 8);                 // sangre en el regazo
            g.DrawLine(penHueso, -9, 1, -2, 7);                    // huesos expuestos
            g.DrawLine(penHueso, 4, 3, 11, 9);
            g.Restore(guardado);
        }

        // ================= PANTALLA DE DIÁLOGO (dibujo) =================
        void DibujarDialogo(Graphics g)
        {
            Npc n = hablando;
            if (n == null) return;

            // Oscurece el mundo para que el retrato y la caja de texto destaquen
            using (var velo = new SolidBrush(Color.FromArgb(165, 0, 0, 0)))
                g.FillRectangle(velo, 0, 0, W * T, H * T);

            // Retrato de la enfermera a la izquierda (cambia según su estado mental)
            Bitmap retrato = ImagenEnfermeraDialogo();
            if (retrato != null)
                g.DrawImage(retrato, new Rectangle(0, 60, 449, 560));

            // La imagen Texto.png ocupa la zona derecha
            float escala = 560f / 604f;
            var caja = new RectangleF(390, 450, 560, 185 * escala);

            if (imgTexto != null)
                g.DrawImage(imgTexto, caja);
            else
            {
                using var fondo = new SolidBrush(Color.FromArgb(235, 8, 8, 12));
                g.FillRectangle(fondo, caja);
                g.DrawRectangle(Pens.DimGray, caja.X, caja.Y, caja.Width, caja.Height);
            }

            // Quién habla en este momento: su nombre se muestra en el recuadro superior
            bool esJugador = true;
            string linea = "...";
            if (dialogo.Count > 0)
            {
                int i = Math.Max(0, Math.Min(dialogoIdx, dialogo.Count - 1));
                esJugador = dialogo[i].EsJugador;
                linea = dialogo[i].Texto;
            }
            string hablante = esJugador ? nombreJugador : NombreNpc(n);

            // Apartado de arriba: nombre de quien habla, centrado en su recuadro
            var nombreRect = new RectangleF(caja.X + 47 * escala, caja.Y + 9 * escala, 146 * escala, 38 * escala);
            using (var fNombre = new Font("Georgia", 13f, FontStyle.Bold))
            using (var sfCentro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var sombra = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                g.DrawString(hablante, fNombre, sombra, new RectangleF(nombreRect.X + 1, nombreRect.Y + 1, nombreRect.Width, nombreRect.Height), sfCentro);
                g.DrawString(hablante, fNombre, Brushes.White, nombreRect, sfCentro);
            }

            // Recuadro grande: empieza por debajo del recuadro del nombre para no taparlo,
            // con el texto algo más grande y centrado (horizontal y verticalmente)
            var textoRect = new RectangleF(caja.X + 30 * escala, caja.Y + 59 * escala,
                                           (578 - 30) * escala, (159 - 59) * escala);

            // La réplica va sin prefijo: el nombre de arriba ya indica quién habla
            string mostrado = "«" + linea + "»";

            using (var fTexto = new Font("Georgia", 16f, FontStyle.Regular))
            using (var sfTexto = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.Word, FormatFlags = StringFormatFlags.LineLimit })
            using (var pincelTexto = new SolidBrush(Color.FromArgb(238, 238, 235)))
                g.DrawString(mostrado, fTexto, pincelTexto, textoRect, sfTexto);

            // Aviso de teclas al pie de la caja
            bool ultima = dialogoIdx >= dialogo.Count - 1;
            string pista = (ultima ? "E / Enter: cerrar" : "E / Enter: siguiente") +
                           (n.Tipo == "destrozado" ? "      Q / Esc: cerrar" : "      1-4: elegir      Q / Esc: cerrar");
            using (var fPista = new Font("Consolas", 10))
            using (var sfPista = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(pista, fPista, Brushes.Gray, new RectangleF(caja.X, caja.Bottom + 3, caja.Width, 14), sfPista);

            // Panel de opciones (el caníbal solo mantiene una conversación)
            if (n.Tipo == "destrozado")
            {
                var panelSer = new Rectangle(390, 305, 560, 118);
                using var fondo = new SolidBrush(Color.FromArgb(220, 12, 5, 5));
                g.FillRectangle(fondo, panelSer);
                g.DrawRectangle(Pens.DarkRed, panelSer);
                using var fTitulo = new Font("Georgia", 15, FontStyle.Bold);
                g.DrawString("EL SER", fTitulo, Brushes.DarkRed, panelSer.X + 18, panelSer.Y + 12);
                using var fTexto = new Font("Georgia", 12);
                g.DrawString("Solo entiende el hambre. Dale un brazo o una pierna...\no aléjate antes de que se canse de esperar.", fTexto, Brushes.Gray,
                    new RectangleF(panelSer.X + 18, panelSer.Y + 40, panelSer.Width - 36, 60));
                return;
            }

            var panel = new Rectangle(390, 262, 560, 172);
            using (var fondo = new SolidBrush(Color.FromArgb(224, 10, 10, 14))) g.FillRectangle(fondo, panel);
            g.DrawRectangle(Pens.DimGray, panel);

            using (var fTitulo = new Font("Georgia", 14, FontStyle.Bold))
                g.DrawString("OPCIONES", fTitulo, Brushes.Gold, panel.X + 18, panel.Y + 10);

            bool pastillasOk = n.Tipo != "dormido" && !n.Sigue && mano == "Frasco de pastillas";
            string[] ops =
            {
                "1) Seguir hablando",
                n.Sigue ? "2) Decirle que se quede aquí"
                        : n.Tipo == "dormido" ? "2) Despertarlo y pedirle que me siga" : "2) Pedirle que me siga",
                n.Tipo == "dormido" || n.Sigue ? "3) —"
                        : pastillasOk ? "3) Ofrecerle las pastillas (le convencen seguro)"
                                      : "3) Ofrecerle pastillas (necesitas el frasco en la mano)",
                TieneFilo ? "4) Cortarle una extremidad" : "4) Cortarle una extremidad (necesitas algo afilado en la mano)"
            };
            bool[] activa = { true, true, pastillasOk, TieneFilo };

            using (var f = new Font("Consolas", 12.5f, FontStyle.Bold))
                for (int i = 0; i < ops.Length; i++)
                    g.DrawString(ops[i], f, activa[i] ? Brushes.Gold : Brushes.DimGray, panel.X + 18, panel.Y + 44 + i * 27);

            using (var pequena = new Font("Consolas", 9.5f))
                g.DrawString("Pulsa 1-4 para elegir", pequena, Brushes.Gray, panel.X + 18, panel.Bottom - 23);
        }

        // Elige el tile transitable más alejado de la enfermera, en la dirección en que el NPC huye
        Point TileHuyendo(PointF desde, PointF amenaza)
        {
            float ax = desde.X - amenaza.X, ay = desde.Y - amenaza.Y;
            float lon = (float)Math.Sqrt(ax * ax + ay * ay);
            if (lon < 1f) { ax = 1f; ay = 0f; lon = 1f; }
            ax /= lon; ay /= lon;

            Point mejor = Tile(desde.X, desde.Y);
            float mejorPunt = float.MinValue;

            for (int ty = 1; ty < H - 1; ty++)
                for (int tx = 1; tx < W - 1; tx++)
                {
                    if (cuarto.Solido(tx, ty)) continue;
                    PointF c = C(tx, ty);
                    float dx = c.X - desde.X, dy = c.Y - desde.Y;
                    float dl = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dl < 1f) continue;

                    float distJugador = Dist(c.X, c.Y, amenaza.X, amenaza.Y);
                    float alineacion = (dx * ax + dy * ay) / dl;   // 1 = justo en la dirección de huida
                    float punt = distJugador + alineacion * 300f;  // prefiere lejos y en línea recta

                    if (punt > mejorPunt) { mejorPunt = punt; mejor = new Point(tx, ty); }
                }

            return mejor;
        }

        Point TileAleatorioCerca(PointF casa)
        {
            Point c = Tile(casa.X, casa.Y);
            for (int intento = 0; intento < 50; intento++)
            {
                int tx = c.X + rnd.Next(-7, 8), ty = c.Y + rnd.Next(-7, 8);
                if (tx < 1 || ty < 1 || tx >= W - 1 || ty >= H - 1) continue;
                if (cuarto.Solido(tx, ty)) continue;
                PointF centro = C(tx, ty);
                if (Dist(centro.X, centro.Y, px, py) < 90) continue;
                return new Point(tx, ty);
            }
            return c;
        }

        // Tecla Q
        void Soltar()
        {
            if (estado != Estado.Jugando || faseFade != 0 || mano == null) return;
            float sx = px + (float)Math.Cos(angulo) * 22, sy = py + (float)Math.Sin(angulo) * 22;
            if (Choca(sx, sy, 4)) { sx = px; sy = py; }
            cuarto.Objetos.Add(new Objeto(mano, new PointF(sx, sy)));
            Mensaje($"Sueltas: {mano}", 2);
            mano = null;
            armaSangrienta = false;
            Sonido(350, 60);
        }

        void UsarPuerta(Puerta p)
        {
            if (p.Requiere != null && !p.Abierta)
            {
                if (mano == p.Requiere)
                {
                    mano = null; armaSangrienta = false; p.Abierta = true;
                    Sonido(700, 150);
                    Mensaje($"Usas: {p.Requiere}. La puerta se abre.", 3);
                }
                else Mensaje($"Está cerrada. Necesito: {p.Requiere}.", 3);
                return;
            }

            puertaUsada = p;
            faseFade = 1;
            Sonido(200, 120);
        }

        void CambiarCuarto(Puerta p)
        {
            // Quienes te siguen (acompañantes, policías y el caníbal suelto, si están cerca) cruzan la puerta contigo
            var viajeros = cuarto.Npcs.FindAll(n => !n.Cortado && !n.Huye &&
                ((n.Sigue && Dist(n.Pos.X, n.Pos.Y, px, py) < 280) ||
                 (n.Tipo == "policia" && Dist(n.Pos.X, n.Pos.Y, px, py) < 420) ||
                 (n.Tipo == "destrozado" && canibalLibre && Dist(n.Pos.X, n.Pos.Y, px, py) < 450)));
            foreach (Npc v in viajeros) cuarto.Npcs.Remove(v);

            actual = p.Destino;
            px = p.Spawn.X; py = p.Spawn.Y;
            entrada = p.Spawn;
            tiempoNombre = 3;
            parpadeo = 0.3f;
            alucinaciones.Clear(); tiempoAlucinacion = 2f;

            if (!conocioCanibal && actual == cuartoCanibal) ConocerCanibal();

            var usados = new HashSet<Point>();
            foreach (Npc v in viajeros)
            {
                bool esCanibal = v == canibalRef;
                PointF pos = PosLibreCerca(p.Spawn, usados, esCanibal ? 110f : 30f);   // el caníbal entra a cierta distancia
                v.Pos = pos; v.Casa = pos;
                v.TieneDestino = false; v.Repath = 0; v.Espera = 0.2f;
                cuarto.Npcs.Add(v);
                if (esCanibal) { cuartoCanibal = actual; canibalComiendo = 1.2f; canibalEspera = 0; }
            }
        }

        // ---------- Colisiones y vista ----------
        bool Solido(float x, float y) => cuarto.Solido((int)Math.Floor(x / T), (int)Math.Floor(y / T));
        bool EsOpaco(float x, float y) => cuarto.Opaco((int)Math.Floor(x / T), (int)Math.Floor(y / T));

        bool Choca(float x, float y, float r)
        {
            if (Solido(x - r, y - r) || Solido(x + r, y - r) || Solido(x - r, y + r) || Solido(x + r, y + r)) return true;
            foreach (Npc n in cuarto.Npcs)
                if (n.Tipo != "dormido" && !n.Cortado && !n.Sigue &&
                    Dist(x, y, n.Pos.X, n.Pos.Y) < (n.Tipo == "destrozado" ? 18 * EscalaCanibal() : 18) + r) return true;
            return false;
        }

        void Mover(ref float x, ref float y, float vx, float vy, float r)
        {
            if (!Choca(x + vx, y, r)) x += vx;
            if (!Choca(x, y + vy, r)) y += vy;
        }

        bool VisionLibre(float x1, float y1, float x2, float y2)
        {
            int pasos = (int)(Dist(x1, y1, x2, y2) / 8);
            for (int i = 1; i < pasos; i++)
            {
                float t = i / (float)pasos;
                if (EsOpaco(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)) return false;
            }
            return true;
        }

        Point Tile(float x, float y) => new Point((int)(x / T), (int)(y / T));

        // Búsqueda en anchura: devuelve el siguiente tile para llegar a 'hasta'
        Point SiguientePaso(Point desde, Point hasta)
        {
            if (desde == hasta) return desde;
            var previo = new Dictionary<Point, Point> { [desde] = desde };
            var cola = new Queue<Point>();
            cola.Enqueue(desde);
            Point[] dirs = { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };

            while (cola.Count > 0)
            {
                Point p = cola.Dequeue();
                if (p == hasta) break;
                foreach (Point d in dirs)
                {
                    var n = new Point(p.X + d.X, p.Y + d.Y);
                    if (previo.ContainsKey(n) || cuarto.Solido(n.X, n.Y)) continue;
                    previo[n] = p;
                    cola.Enqueue(n);
                }
            }

            if (!previo.ContainsKey(hasta)) return desde;
            Point cur = hasta;
            while (previo[cur] != desde) cur = previo[cur];
            return cur;
        }

        // ---------- Utilidades ----------
        static float Dist(float ax, float ay, float bx, float by) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        static float Rad(float grados) => grados * (float)Math.PI / 180f;

        // Gira un ángulo hacia otro por el camino más corto
        static float GirarHacia(float actual, float objetivo, float maxPaso)
        {
            float dif = objetivo - actual;
            while (dif > Math.PI) dif -= (float)(Math.PI * 2);
            while (dif < -Math.PI) dif += (float)(Math.PI * 2);
            if (Math.Abs(dif) <= maxPaso) return objetivo;
            return actual + Math.Sign(dif) * maxPaso;
        }

        bool Pulsada(Keys k) => teclas.Contains(k);

        void Mensaje(string texto, float segundos) { mensaje = texto; tiempoMensaje = segundos; }

        // Los sonidos van en otro hilo porque Console.Beep bloquea
        void Sonido(int frecuencia, int ms)
        {
            Task.Run(() => { try { Console.Beep(frecuencia, ms); } catch { } });
        }

        // ================= ENTRADA =================
        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool nueva = teclas.Add(e.KeyCode);   // true solo la primera vez (ignora la repetición al mantener pulsada)

            if (estado == Estado.Titulo)
            {
                if (nueva) EntradaNombre(e);
                base.OnKeyDown(e);
                return;
            }

            if (eligiendoCarta)                   // pantalla de cartas: el mundo está detenido
            {
                if (nueva)
                {
                    if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) ElegirCarta(0);
                    else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) ElegirCarta(1);
                    else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) ElegirCarta(2);
                }
                base.OnKeyDown(e);
                return;
            }

            if (hablando != null)                 // diálogo abierto: las teclas son para avanzar o elegir
            {
                if (nueva) ManejarTeclaMenu(e.KeyCode);
                base.OnKeyDown(e);
                return;
            }
            if (nueva)
            {
                if (e.KeyCode == Keys.R && estado == Estado.Descubierto) { Reiniciar(); estado = Estado.Jugando; }
                if (e.KeyCode == Keys.F && estado == Estado.Jugando) linterna = !linterna;
                if (e.KeyCode == Keys.E) Interactuar();
                if (e.KeyCode == Keys.X) UsarFilo();
                if (e.KeyCode == Keys.Q) Soltar();
            }
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        // Escribe el nombre del jugador en la pantalla de título
        void EntradaNombre(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                nombreJugador = nombreJugador.Trim();
                if (string.IsNullOrWhiteSpace(nombreJugador)) nombreJugador = "Enfermera";
                Reiniciar();
                estado = Estado.Jugando;
                return;
            }
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.Back)
            {
                if (nombreJugador.Length > 0)
                    nombreJugador = nombreJugador.Substring(0, nombreJugador.Length - 1);
                return;
            }

            string t = "";
            if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                t = ((char)('A' + (e.KeyCode - Keys.A))).ToString();
            else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
                t = ((char)('0' + (e.KeyCode - Keys.D0))).ToString();
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                t = ((char)('0' + (e.KeyCode - Keys.NumPad0))).ToString();
            else if (e.KeyCode == Keys.Space)
                t = " ";

            if (t != "" && nombreJugador.Length < 16)
                nombreJugador += e.Shift ? t.ToUpper() : t.ToLower();
        }

        protected override void OnKeyUp(KeyEventArgs e) { teclas.Remove(e.KeyCode); base.OnKeyUp(e); }
        protected override void OnDeactivate(EventArgs e) { teclas.Clear(); base.OnDeactivate(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (estado == Estado.Jugando) linterna = !linterna;
            base.OnMouseDown(e);
        }

        // ================= DIBUJO =================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);

            if (estado == Estado.Titulo) { DibujarTitulo(g); return; }

            // Solo se dibuja la habitación actual: cada habitación es una escena
            GraphicsState guardado = g.Save();
            if (sacudida > 0) g.TranslateTransform(rnd.Next(-5, 6), rnd.Next(-5, 6));
            DibujarMundo(g);
            DibujarAmbiente(g);
            DibujarOscuridad(g);
            DibujarAlucinaciones(g);
            DibujarMarcasOjoDelSer(g);
            DibujarGlowLinterna(g);
            DibujarBrillos(g);
            g.Restore(guardado);

            DibujarPantalla(g);

            // La cámara del caníbal se tiñe de rojo a medida que come
            if (actual == 2 && partesServidas > 0 && estado == Estado.Jugando)
            {
                int ar = (int)(Math.Min(110, partesServidas * 8) * (0.7 + 0.3 * Math.Sin(tiempo * 3)));
                using var tinte = new SolidBrush(Color.FromArgb(ar, 140, 0, 0));
                g.FillRectangle(tinte, 0, 0, W * T, H * T);
            }

            // Luces de la policía
            if (policiaLlamada && estado == Estado.Jugando)
            {
                Color luzPoli = (int)(tiempo * 4) % 2 == 0 ? Color.FromArgb(30, 220, 0, 0) : Color.FromArgb(30, 0, 60, 255);
                using var destello = new SolidBrush(luzPoli);
                g.FillRectangle(destello, 0, 0, W * T, H * T);
            }

            // La locura tiñe la pantalla de violeta cuando pierdes la cordura
            if (enloquecido && estado == Estado.Jugando)
            {
                int ai = (int)(35 + 25 * Math.Sin(tiempo * 5));
                using var locuraTinte = new SolidBrush(Color.FromArgb(Math.Max(10, ai), 90, 0, 90));
                g.FillRectangle(locuraTinte, 0, 0, W * T, H * T);
            }

            // Las alucinaciones distorsionan la pantalla mientras están cerca
            if (alucinaciones.Count > 0 && estado == Estado.Jugando)
            {
                int ai = (int)(22 + 18 * Math.Sin(tiempo * 8));
                using var tinteAluc = new SolidBrush(Color.FromArgb(Math.Max(8, ai), 70, 0, 110));
                g.FillRectangle(tinteAluc, 0, 0, W * T, H * T);
            }

            DibujarHUD(g);
            if (hablando != null && estado == Estado.Jugando) DibujarDialogo(g);
            if (eligiendoCarta) DibujarCartas(g);

            if (estado == Estado.Descubierto)
            {
                g.FillRectangle(brOscuro, 0, 0, W * T, H * T);
                Color colorTitulo = tituloPerder.Contains("ALUCINACIONES") ? Color.MediumPurple
                                  : tituloPerder.Contains("CANÍBAL") ? Color.DarkRed
                                  : Color.DodgerBlue;
                Texto(g, tituloPerder, 44, colorTitulo, 220);
                Texto(g, motivoPerder, 18, Color.Gray, 295);
                Texto(g, $"Alertas: {alertados}  —  Pulsa R para intentarlo de nuevo", 18, Color.Gray, 335);
            }

            // Nombre de la habitación al entrar
            if (tiempoNombre > 0 && estado == Estado.Jugando && hablando == null)
            {
                int a = (int)(Math.Min(1f, tiempoNombre) * 255);
                Texto(g, cuarto.Nombre.ToUpper(), 30, Color.FromArgb(a, 210, 210, 210), 90);
            }

            // Fundido a negro al cambiar de habitación
            if (fade > 0)
            {
                using var negro = new SolidBrush(Color.FromArgb((int)(Math.Min(1f, fade) * 255), 0, 0, 0));
                g.FillRectangle(negro, 0, 0, W * T, H * T);
            }
        }

        void DibujarMundo(Graphics g)
        {
            Cuarto c = cuarto;
            if (c.Fondo == null) c.Fondo = ConstruirFondo(c);   // suelo, paredes y muebles se dibujan una sola vez
            g.DrawImageUnscaled(c.Fondo, 0, 0);

            foreach (Puerta pu in c.Puertas) DibujarPuerta(g, new Rectangle(pu.Tile.X * T, pu.Tile.Y * T, T, T), c);
            foreach (RectangleF m in c.Manchas) DibujarMancha(g, m);
            foreach (Cortable k in c.Cortables) DibujarCortable(g, k);
            foreach (Objeto o in c.Objetos) DibujarObjeto(g, o.Nombre, o.Pos.X, o.Pos.Y);

            // De atrás hacia delante: primero los que están más arriba que la enfermera, luego ella y después los demás
            foreach (Npc n in c.Npcs) if (n.Pos.Y <= py) DibujarNpc(g, n);
            DibujarEnfermera(g);
            foreach (Npc n in c.Npcs) if (n.Pos.Y > py) DibujarNpc(g, n);
        }

        void DibujarSombra(Graphics g, float x, float y, float ancho)
        {
            g.FillEllipse(brSombra, x - ancho / 2, y - ancho * 0.2f, ancho, ancho * 0.4f);
        }

        // Mancha de sangre con un brillo húmedo en el centro
        void DibujarMancha(Graphics g, RectangleF m)
        {
            g.FillEllipse(brSangre, m);
            g.FillEllipse(brSangreViva, m.X + m.Width * 0.2f, m.Y + m.Height * 0.15f, m.Width * 0.55f, m.Height * 0.5f);
        }

        // ---------- Fondo de la habitación (se dibuja una sola vez y se guarda) ----------
        Bitmap ConstruirFondo(Cuarto c)
        {
            var bmp = new Bitmap(W * T, H * T);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int semilla = 17;
            foreach (char ch in c.Nombre) semilla = semilla * 31 + ch;
            var r = new Random(semilla);
            Color f1 = ((SolidBrush)c.Suelo1).Color, f2 = ((SolidBrush)c.Suelo2).Color, cp = ((SolidBrush)c.Pared).Color;

            // 1) suelo y paredes (las puertas se pintan encima con su propio estado)
            for (int ty = 0; ty < H; ty++)
                for (int tx = 0; tx < W; tx++)
                {
                    var rc = new Rectangle(tx * T, ty * T, T, T);
                    char ch = c.Mapa[ty][tx];
                    if (ch == '#' || ch == 'D') DibujarParedFondo(g, rc, cp, r);
                    else DibujarSueloFondo(g, c, rc, (tx + ty) % 2 == 0 ? f1 : f2, r);
                }

            // 2) sombra que proyecta cada pared sobre el suelo
            for (int ty = 0; ty < H; ty++)
                for (int tx = 0; tx < W; tx++)
                {
                    if (c.Mapa[ty][tx] != '#') continue;
                    if (ty + 1 < H && c.Mapa[ty + 1][tx] != '#' && c.Mapa[ty + 1][tx] != 'D')
                    {
                        var rs = new Rectangle(tx * T, (ty + 1) * T, T, 16);
                        using var lg = new LinearGradientBrush(rs, Color.FromArgb(120, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f);
                        g.FillRectangle(lg, rs);
                    }
                    if (tx + 1 < W && c.Mapa[ty][tx + 1] != '#' && c.Mapa[ty][tx + 1] != 'D')
                    {
                        var rs = new Rectangle((tx + 1) * T, ty * T, 8, T);
                        using var lg = new LinearGradientBrush(rs, Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f);
                        g.FillRectangle(lg, rs);
                    }
                }

            // 3) muebles: primero sus sombras y luego ellos
            for (int pasada = 0; pasada < 2; pasada++)
                for (int ty = 0; ty < H; ty++)
                    for (int tx = 0; tx < W; tx++)
                    {
                        char ch = c.Mapa[ty][tx];
                        if (ch == '.' || ch == '#' || ch == 'D') continue;
                        var rc = new Rectangle(tx * T, ty * T, T, T);
                        if (pasada == 0)
                        {
                            if (ch == 'k') g.FillEllipse(brSombra, rc.X + 8, rc.Y + 24, 26, 12);
                            else g.FillRectangle(brSombra, rc.X + 4, rc.Y + 6, T, T - 2);
                        }
                        else DibujarMueble(g, c, tx, ty, rc);
                    }
            return bmp;
        }

        static int Lim(int v) => Math.Max(0, Math.Min(255, v));

        void DibujarSueloFondo(Graphics g, Cuarto c, Rectangle rc, Color b, Random r)
        {
            int v = r.Next(-7, 8);
            using (var br = new SolidBrush(Color.FromArgb(Lim(b.R + v), Lim(b.G + v), Lim(b.B + v))))
                g.FillRectangle(br, rc);

            if (c.Exterior)
            {
                // césped y tierra: briznas de hierba y alguna zona pelada
                for (int i = 0; i < 10; i++)
                {
                    int gx = rc.X + r.Next(T), gy = rc.Y + 6 + r.Next(T - 6);
                    using var pen = new Pen(Color.FromArgb(110, 25 + r.Next(30), 60 + r.Next(60), 25), 1.4f);
                    g.DrawLine(pen, gx, gy, gx + r.Next(-2, 3), gy - r.Next(3, 7));
                }
                if (r.Next(6) == 0)
                {
                    using var tierra = new SolidBrush(Color.FromArgb(45, 40, 25, 15));
                    g.FillEllipse(tierra, rc.X + r.Next(20), rc.Y + r.Next(20), 14 + r.Next(10), 8 + r.Next(6));
                }
            }
            else
            {
                // juntas entre baldosas, reflejo suave, rayones y suciedad
                g.DrawLine(penJunta, rc.X, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
                g.DrawLine(penJunta, rc.Right - 1, rc.Y, rc.Right - 1, rc.Bottom);
                g.DrawLine(penBrilloSuelo, rc.X + 1, rc.Y + 1, rc.Right - 2, rc.Y + 1);
                g.DrawLine(penBrilloSuelo, rc.X + 1, rc.Y + 1, rc.X + 1, rc.Bottom - 2);
                if (r.Next(5) == 0)
                {
                    int x1 = rc.X + r.Next(T), y1 = rc.Y + r.Next(T);
                    g.DrawLine(penGrieta, x1, y1, x1 + r.Next(-12, 13), y1 + r.Next(-6, 7));
                }
                if (r.Next(10) == 0)
                {
                    using var suciedad = new SolidBrush(Color.FromArgb(28, 0, 0, 0));
                    g.FillEllipse(suciedad, rc.X + r.Next(18), rc.Y + r.Next(18), 12 + r.Next(14), 8 + r.Next(10));
                }
            }
        }

        void DibujarParedFondo(Graphics g, Rectangle rc, Color cp, Random r)
        {
            Color arriba = Color.FromArgb(Lim(cp.R + 22), Lim(cp.G + 22), Lim(cp.B + 22));
            Color abajo = Color.FromArgb(Lim(cp.R - 30), Lim(cp.G - 30), Lim(cp.B - 30));
            using (var lg = new LinearGradientBrush(rc, arriba, abajo, 90f)) g.FillRectangle(lg, rc);
            g.DrawLine(penBiselClaro, rc.X, rc.Y + 1, rc.Right, rc.Y + 1);
            g.DrawLine(penBiselOscuro, rc.X, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
            g.DrawLine(penBiselOscuro, rc.Right - 1, rc.Y, rc.Right - 1, rc.Bottom);
            using (var zocalo = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(zocalo, rc.X, rc.Bottom - 9, T, 8);

            if (r.Next(5) == 0)   // grietas
            {
                int gx = rc.X + 6 + r.Next(T - 12), gy = rc.Y + 4;
                for (int i = 0; i < 4; i++)
                {
                    int nx = gx + r.Next(-5, 6), ny = gy + 7 + r.Next(0, 4);
                    g.DrawLine(penGrieta, gx, gy, nx, ny);
                    gx = nx; gy = ny;
                }
            }
            if (r.Next(7) == 0)   // humedad que escurre desde arriba
            {
                var rh = new Rectangle(rc.X + r.Next(T - 14), rc.Y, 14, T - 4);
                using var hl = new LinearGradientBrush(rh, Color.FromArgb(70, 20, 25, 20), Color.FromArgb(0, 20, 25, 20), 90f);
                g.FillRectangle(hl, rh);
            }
        }

        void DibujarMueble(Graphics g, Cuarto c, int tx, int ty, Rectangle r)
        {
            switch (c.Mapa[ty][tx])
            {
                case 'b':   // cama: marco, colchón, almohada y manta con pliegues
                {
                    bool cabecera = tx == 0 || c.Mapa[ty][tx - 1] != 'b';
                    using (var marco = new SolidBrush(Color.FromArgb(120, 125, 135))) g.FillRectangle(marco, r.X + 1, r.Y + 3, T - 2, T - 6);
                    g.FillRectangle(brColchon, r.X + 3, r.Y + 5, T - 6, T - 10);
                    if (cabecera)
                    {
                        g.FillEllipse(Brushes.White, r.X + 5, r.Y + 9, 16, 22);
                        using (var luz = new SolidBrush(Color.FromArgb(40, 0, 0, 40))) g.FillEllipse(luz, r.X + 11, r.Y + 13, 9, 16);
                    }
                    else
                    {
                        using (var manta = new SolidBrush(Color.FromArgb(150, 170, 195))) g.FillRectangle(manta, r.X + 3, r.Y + 5, T - 6, T - 10);
                        g.DrawLine(penBiselClaro, r.X + 6, r.Y + 13, r.Right - 5, r.Y + 13);
                        g.DrawLine(penBiselOscuro, r.X + 6, r.Y + 20, r.Right - 5, r.Y + 20);
                        g.DrawLine(penBiselClaro, r.X + 6, r.Y + 27, r.Right - 5, r.Y + 27);
                    }
                    g.DrawRectangle(penMueble, r.X + 1, r.Y + 3, T - 2, T - 6);
                    break;
                }
                case 'm':   // mesa de madera con vetas
                {
                    var rm = new Rectangle(r.X + 3, r.Y + 3, T - 6, T - 6);
                    using (var madera = new LinearGradientBrush(rm, Color.FromArgb(135, 95, 60), Color.FromArgb(95, 62, 38), 90f)) g.FillRectangle(madera, rm);
                    g.DrawLine(penBiselOscuro, r.X + 6, r.Y + 13, r.Right - 6, r.Y + 13);
                    g.DrawLine(penBiselOscuro, r.X + 6, r.Y + 24, r.Right - 6, r.Y + 24);
                    g.DrawLine(penBiselClaro, r.X + 4, r.Y + 4, r.Right - 5, r.Y + 4);
                    g.DrawRectangle(penMueble, rm);
                    break;
                }
                case 'a':   // armario / estante metálico
                {
                    var ra = new Rectangle(r.X + 2, r.Y + 2, T - 4, T - 4);
                    using (var metal = new LinearGradientBrush(ra, Color.FromArgb(150, 165, 175), Color.FromArgb(85, 98, 108), 90f)) g.FillRectangle(metal, ra);
                    g.DrawLine(penMueble, r.X + T / 2, r.Y + 2, r.X + T / 2, r.Y + T - 2);
                    g.FillRectangle(Brushes.Silver, r.X + T / 2 - 5, r.Y + 17, 2, 7);
                    g.FillRectangle(Brushes.Silver, r.X + T / 2 + 3, r.Y + 17, 2, 7);
                    g.DrawLine(penBiselClaro, r.X + 3, r.Y + 3, r.Right - 4, r.Y + 3);
                    g.DrawRectangle(penMueble, ra);
                    break;
                }
                case 'o':   // mesa de operaciones
                case 'z':   // plancha de la morgue
                {
                    Color a1 = c.Mapa[ty][tx] == 'o' ? Color.FromArgb(185, 200, 208) : Color.FromArgb(160, 175, 190);
                    using (var acero = new LinearGradientBrush(r, a1, Color.FromArgb(105, 120, 132), 45f)) g.FillRectangle(acero, r);
                    g.DrawLine(penBiselClaro, r.X + 2, r.Y + 2, r.Right - 2, r.Y + 2);
                    g.DrawLine(penBiselOscuro, r.X + 6, r.Y + T / 2, r.Right - 6, r.Y + T / 2);   // canal para los líquidos
                    g.DrawRectangle(penMueble, r);
                    break;
                }
                case 'k':   // silla
                    g.FillRectangle(brMesa, r.X + 9, r.Y + 12, 22, 20);
                    g.FillRectangle(brMesa, r.X + 9, r.Y + 7, 22, 6);
                    g.DrawRectangle(penMueble, r.X + 9, r.Y + 12, 22, 20);
                    g.DrawRectangle(penMueble, r.X + 9, r.Y + 7, 22, 6);
                    break;
                case 'c':   // caja de cartón con solapas y etiqueta
                    g.FillRectangle(brCarton, r.X + 4, r.Y + 4, T - 8, T - 8);
                    g.DrawLine(penBiselOscuro, r.X + 6, r.Y + 14, r.Right - 6, r.Y + 14);
                    g.DrawLine(penBiselClaro, r.X + 5, r.Y + 5, r.Right - 6, r.Y + 5);
                    g.FillRectangle(Brushes.WhiteSmoke, r.X + 22, r.Y + 24, 10, 7);
                    g.DrawRectangle(penMueble, r.X + 4, r.Y + 4, T - 8, T - 8);
                    break;
            }
        }

        // La puerta cambia (se abre, se cierra con llave), así que se dibuja en cada cuadro
        void DibujarPuerta(Graphics g, Rectangle r, Cuarto c)
        {
            Puerta p = c.Puertas.Find(q => q.Tile.X == r.X / T && q.Tile.Y == r.Y / T);
            bool cerrada = p != null && p.Requiere != null && !p.Abierta;

            using (var marco = new SolidBrush(Color.FromArgb(55, 38, 28))) g.FillRectangle(marco, r);
            var hoja = new Rectangle(r.X + 3, r.Y + 3, T - 6, T - 6);
            using (var madera = new LinearGradientBrush(hoja, Color.FromArgb(150, 100, 62), Color.FromArgb(100, 64, 40), 90f)) g.FillRectangle(madera, hoja);
            g.DrawRectangle(penMueble, hoja);
            g.DrawRectangle(penBiselOscuro, hoja.X + 5, hoja.Y + 5, hoja.Width - 10, hoja.Height / 2 - 7);
            g.DrawRectangle(penBiselOscuro, hoja.X + 5, hoja.Y + hoja.Height / 2 + 2, hoja.Width - 10, hoja.Height / 2 - 8);
            g.FillEllipse(Brushes.Gold, r.X + T / 2 - 3, r.Y + T / 2 - 3, 6, 6);
            if (cerrada)
            {
                g.FillRectangle(Brushes.Firebrick, r.X + T / 2 - 6, r.Y + 5, 12, 5);
                g.DrawRectangle(Pens.Black, r.X + T / 2 - 6, r.Y + 5, 12, 5);
            }
            if (p != null && p.Destino == 5)   // letrero de SALIDA
            {
                var letrero = new Rectangle(r.X, r.Y + 24, T, 12);
                using (var fondo = new SolidBrush(cerrada ? Color.FromArgb(110, 120, 0, 0) : Color.FromArgb(120, 0, 110, 30))) g.FillRectangle(fondo, letrero);
                using var f = new Font("Arial", 7, FontStyle.Bold);
                g.DrawString("SALIDA", f, cerrada ? Brushes.Firebrick : Brushes.LimeGreen, r.X, r.Y + 24);
            }
        }

        // Ambiente: color de cada habitación y motas de polvo (solo se ven donde llega la luz)
        void DibujarAmbiente(Graphics g)
        {
            if (cuarto.Tinte.A > 0)
            {
                using var tinte = new SolidBrush(cuarto.Tinte);
                g.FillRectangle(tinte, 0, 0, W * T, H * T);
            }
            foreach (float[] m in polvo)
            {
                int a = (int)(55 + 45 * Math.Sin(tiempo * 2 + m[4]));
                using var b = new SolidBrush(Color.FromArgb(Math.Max(10, a), 235, 235, 220));
                g.FillEllipse(b, m[0], m[1], m[5], m[5]);
            }
        }

        // La linterna tiñe de un tono cálido la zona que ilumina
        void DibujarGlowLinterna(Graphics g)
        {
            if (!(linterna && parpadeo <= 0)) return;
            using var cono = PoligonoLuz(52, 300, 40);
            using var calido = new SolidBrush(Color.FromArgb(20, 255, 235, 170));
            g.FillPath(calido, cono);
        }

        // Viñeta (roja si el caníbal anda cerca) y grano de película
        void DibujarPantalla(Graphics g)
        {
            g.DrawImageUnscaled(Vineta, 0, 0);
            if (cercaniaCanibal > 0.1f)
            {
                int ar = (int)(cercaniaCanibal * 85 * (0.6 + 0.4 * Math.Sin(tiempo * 9)));
                using var rojo = new SolidBrush(Color.FromArgb(ar, 150, 0, 0));
                g.FillRectangle(rojo, 0, 0, W * T, H * T);
            }
            using var tb = new TextureBrush(Ruido, WrapMode.Tile);
            tb.TranslateTransform(rnd.Next(Ruido.Width), rnd.Next(Ruido.Height));
            g.FillRectangle(tb, 0, 0, W * T, H * T);
        }

        void DibujarCortable(Graphics g, Cortable c)
        {
            float x = c.Pos.X, y = c.Pos.Y;
            switch (c.Tipo)
            {
                case "vendas":
                    if (!c.Cortado)
                    {
                        g.DrawLine(penVenda, x - 20, y - 14, x + 20, y + 10);
                        g.DrawLine(penVenda, x - 20, y + 10, x + 20, y - 14);
                        g.DrawLine(penVenda, x - 20, y - 2, x + 20, y - 2);
                    }
                    else
                    {
                        g.DrawLine(penVenda, x - 20, y - 14, x - 10, y - 4);
                        g.DrawLine(penVenda, x + 20, y - 14, x + 10, y - 4);
                    }
                    break;
                case "caja":
                    if (!c.Cortado)
                    {
                        g.DrawLine(penCinta, x - 16, y, x + 16, y);
                        g.DrawLine(penCinta, x, y - 16, x, y + 16);
                    }
                    else
                    {
                        g.DrawLine(penMueble, x - 16, y - 16, x - 10, y - 8);
                        g.DrawLine(penMueble, x + 16, y - 16, x + 10, y - 8);
                    }
                    break;
                case "bolsa":
                    g.FillRectangle(c.Cortado ? Brushes.DimGray : Brushes.Black, x - 38, y - 13, 76, 26);
                    if (!c.Cortado) g.DrawLine(Pens.Gray, x - 34, y, x + 34, y);
                    else g.FillEllipse(brSangre, x - 8, y - 6, 16, 12);
                    break;
            }
        }

        void DibujarObjeto(Graphics g, string nombre, float x, float y)
        {
            switch (nombre)
            {
                case "Tijeras":
                    g.DrawLine(penAcero, x - 6, y - 9, x + 3, y + 4);
                    g.DrawLine(penAcero, x + 6, y - 9, x - 3, y + 4);
                    g.FillEllipse(Brushes.Crimson, x - 9, y + 3, 7, 7);
                    g.FillEllipse(Brushes.Crimson, x + 2, y + 3, 7, 7);
                    break;
                case "Bisturí":
                    g.FillPolygon(Brushes.Silver, new[] { new PointF(x - 12, y), new PointF(x - 3, y - 3), new PointF(x - 3, y + 3) });
                    g.FillRectangle(Brushes.DimGray, x - 3, y - 2, 14, 4);
                    break;
                case "Llave de la morgue":
                    g.FillEllipse(Brushes.Gold, x - 9, y - 4, 8, 8);
                    g.FillRectangle(Brushes.Gold, x - 2, y - 1, 12, 3);
                    g.FillRectangle(Brushes.Gold, x + 6, y + 1, 2, 4);
                    break;
                case "Llave del exterior":
                    g.FillEllipse(Brushes.OrangeRed, x - 10, y - 5, 10, 10);
                    g.FillRectangle(Brushes.Silver, x - 2, y - 1, 14, 3);
                    g.FillRectangle(Brushes.Silver, x + 7, y + 1, 2, 5);
                    g.FillRectangle(Brushes.Silver, x + 10, y + 1, 2, 4);
                    break;
                case "Cuchillo de enfermería":
                    g.FillPolygon(Brushes.Gainsboro, new[] { new PointF(x - 13, y + 2), new PointF(x + 2, y - 4), new PointF(x + 2, y + 3) });
                    g.FillRectangle(Brushes.Crimson, x + 2, y - 3, 11, 5);
                    break;
                case "Jeringa":
                    g.FillRectangle(Brushes.LightGray, x - 8, y - 2, 12, 4);
                    g.DrawLine(penAcero, x + 4, y, x + 11, y);
                    g.DrawLine(penAcero, x - 8, y, x - 12, y);
                    break;
                case "Frasco de pastillas":
                    g.FillRectangle(Brushes.DarkOrange, x - 4, y - 6, 8, 12);
                    g.FillRectangle(Brushes.White, x - 4, y - 8, 8, 3);
                    break;
                case "Pastillas de cordura":
                    g.FillRectangle(Brushes.White, x - 5, y - 7, 10, 14);
                    g.FillRectangle(Brushes.DodgerBlue, x - 5, y - 9, 10, 4);
                    g.FillEllipse(Brushes.DodgerBlue, x - 7, y - 3, 6, 6);
                    g.FillEllipse(Brushes.DeepSkyBlue, x + 1, y + 1, 6, 6);
                    break;
                case "Brazo amputado":
                    g.FillRectangle(brPiel, x - 11, y - 3, 22, 6);
                    g.FillEllipse(brSangre, x + 7, y - 4, 9, 8);
                    g.FillEllipse(Brushes.DarkRed, x - 14, y - 2, 5, 5);
                    break;
                case "Pierna amputada":
                    g.FillRectangle(brPiel, x - 9, y - 4, 19, 8);
                    g.FillEllipse(brSangre, x + 7, y - 5, 10, 10);
                    g.FillEllipse(Brushes.DarkRed, x - 12, y - 3, 6, 6);
                    break;
            }
        }

        void DibujarNpc(Graphics g, Npc n)
        {
            float x = n.Pos.X, y = n.Pos.Y;
            if (n.Tipo == "destrozado") { DibujarCanibal(g, n); return; }

            if (n.Grito > 0f)
            {
                using var fGrito = new Font("Arial", 11, FontStyle.Bold);
                float s = (float)Math.Sin(tiempo * 18) * 1.5f;
                g.DrawString("¡AAAH!", fGrito, Brushes.Firebrick, x - 22, y - 50 + s);
            }

            using var bata = new SolidBrush(n.Bata);
            using var piel = new SolidBrush(Pieles[n.Variante % Pieles.Length]);
            using var pelo = new SolidBrush(Pelos[(n.Variante / 7) % Pelos.Length]);

            if (n.Tipo == "dormido" || n.Cortado)   // acostado (dormido) o ya muerto
            {
                if (n.Cortado)
                {
                    DibujarMancha(g, new RectangleF(x - 28, y - 6, 66, 26));
                    DibujarMancha(g, new RectangleF(x - 8, y + 2, 36, 15));
                }
                g.FillRectangle(bata, x - 14, y - 10, 46, 20);                      // sábana
                g.DrawLine(penBiselClaro, x - 10, y - 5, x + 28, y - 5);
                g.DrawLine(penBiselOscuro, x - 10, y + 4, x + 28, y + 4);
                g.FillEllipse(piel, x - 30, y - 9, 18, 18);                         // cabeza
                g.FillPie(pelo, x - 31, y - 10, 20, 20, 90, 180);
                if (n.Cortado)
                {
                    g.DrawLine(Pens.Black, x - 25, y - 4, x - 21, y);               // ojos en cruz
                    g.DrawLine(Pens.Black, x - 21, y - 4, x - 25, y);
                    g.DrawLine(Pens.Black, x - 25, y + 2, x - 21, y + 6);
                    g.DrawLine(Pens.Black, x - 21, y + 2, x - 25, y + 6);
                    g.FillEllipse(brSangreViva, x + 2, y - 3, 22, 11);
                }
                else
                {
                    g.DrawLine(Pens.Black, x - 25, y - 3, x - 21, y - 3);           // ojos cerrados
                    g.DrawLine(Pens.Black, x - 25, y + 3, x - 21, y + 3);
                }
                if (n.Tipo == "comido")   // cuerpo a medio comer: se le ven los huesos y las costillas
                {
                    g.DrawLine(penHueso, x - 10, y - 4, x + 6, y + 6);
                    g.DrawLine(penHueso, x + 2, y - 8, x + 14, y + 2);
                    g.DrawLine(penHueso, x + 10, y - 7, x + 22, y + 3);
                    g.FillEllipse(Brushes.DarkRed, x - 4, y - 3, 16, 9);
                }
                return;
            }

            // De pie o sentado: sus ojos hundidos siguen a la enfermera
            float bob = n.TieneDestino && n.Espera <= 0 ? (float)Math.Sin(n.Fase * 8) * 1.5f : 0;
            if (n.Huye)   // tiembla mientras huye
            {
                x += (float)Math.Sin(tiempo * 31) * 1.6f;
                y += (float)Math.Sin(tiempo * 23) * 1.2f;
            }
            float a = (float)Math.Atan2(py - y, px - x);
            float ox = (float)Math.Cos(a) * 1.6f, oy = (float)Math.Sin(a) * 1.6f;
            int brazos = Math.Max(0, n.Extremidades - 2);                  // brazos que le quedan
            int piernas = n.Extremidades >= 2 ? 2 : n.Extremidades;        // piernas que le quedan

            DibujarSombra(g, x, y + 22, 32);

            for (int i = 0; i < 2; i++)   // piernas (o muñones)
            {
                float lx = x - 7 + i * 9;
                if (i < piernas)
                {
                    g.FillRectangle(piel, lx, y + 18 + bob, 5, 9);
                    g.FillRectangle(Brushes.DimGray, lx - 1, y + 25 + bob, 7, 3);
                }
                else g.FillEllipse(brSangreViva, lx - 1, y + 18 + bob, 7, 5);
            }

            using (var penBr = new Pen(piel.Color, 4))
                for (int i = 0; i < 2; i++)   // brazos (o muñones)
                {
                    float sx = i == 0 ? x - 12 : x + 12, dirB = i == 0 ? -1 : 1;
                    if (i < brazos)
                    {
                        g.DrawLine(penBr, sx, y - 2 + bob, sx + dirB * 4, y + 13 + bob);
                        g.FillEllipse(piel, sx + dirB * 4 - 3, y + 11 + bob, 6, 6);
                    }
                    else g.FillEllipse(brSangreViva, sx - 3, y - 4 + bob, 7, 7);
                }

            g.FillEllipse(bata, x - 12, y - 8 + bob, 24, 28);                                // bata
            g.FillEllipse(brSombraSuave, x - 2, y - 6 + bob, 14, 24);
            g.DrawLine(penBiselOscuro, x, y - 1 + bob, x, y + 18 + bob);
            g.DrawLine(penBiselClaro, x - 8, y + 2 + bob, x - 8, y + 16 + bob);
            g.DrawLine(penBiselOscuro, x - 6, y - 7 + bob, x, y - 1 + bob);                  // cuello en V
            g.DrawLine(penBiselOscuro, x + 6, y - 7 + bob, x, y - 1 + bob);
            if (n.Tipo == "policia")
            {
                g.FillRectangle(Brushes.Black, x - 12, y + 9 + bob, 24, 3);                  // cinturón
                g.FillEllipse(Brushes.Gold, x + 3, y - 1 + bob, 5, 5);                       // placa
            }

            g.FillEllipse(piel, x - 9, y - 22 + bob, 18, 18);                                // cabeza
            g.FillPie(pelo, x - 10, y - 24 + bob, 20, 17, 180, 180);
            g.FillEllipse(brOjera, x - 8, y - 15 + bob, 7, 5);                               // ojeras
            g.FillEllipse(brOjera, x + 1, y - 15 + bob, 7, 5);
            g.FillEllipse(Brushes.White, x - 7, y - 15 + bob, 5, 4);
            g.FillEllipse(Brushes.White, x + 2, y - 15 + bob, 5, 4);
            g.FillEllipse(Brushes.Black, x - 6 + ox, y - 15 + bob + oy, 2.6f, 3);
            g.FillEllipse(Brushes.Black, x + 3 + ox, y - 15 + bob + oy, 2.6f, 3);
            if (n.Huye) g.FillEllipse(Brushes.Black, x - 2, y - 9 + bob, 4, 4);             // boca abierta de pánico
            else g.DrawLine(penBiselOscuro, x - 2, y - 8 + bob, x + 2, y - 8 + bob);

            if (n.Tipo == "policia")   // gorra y luz de la sirena
            {
                g.FillRectangle(Brushes.Navy, x - 10, y - 26 + bob, 20, 6);
                g.FillRectangle(Brushes.Black, x - 11, y - 21 + bob, 22, 2);
                g.FillEllipse((int)(tiempo * 6) % 2 == 0 ? Brushes.Red : Brushes.DodgerBlue, x - 3, y - 34 + bob, 6, 6);
            }
            else if (n.Sigue)          // halo dorado: te está siguiendo
            {
                using var halo = new Pen(Color.FromArgb(200, 255, 200, 40), 2);
                g.DrawEllipse(halo, x - 8, y - 34 + bob, 16, 5);
            }

            if (n.Sangrando)   // sangre cayendo del muñón
            {
                g.FillEllipse(brSangreViva, x - 6, y + 14, 12, 6);
                using var gota = new Pen(Color.FromArgb(190, 120, 10, 10), 1.5f);
                g.DrawLine(gota, x, y + 19, x, y + 26 + (float)Math.Sin(n.Fase * 6) * 2);
            }

            if (n.Huye)   // signo de pánico sobre la cabeza
            {
                float s = (float)Math.Sin(tiempo * 12) * 2f;
                using var f = new Font("Arial", 14, FontStyle.Bold);
                g.DrawString("!", f, Brushes.Firebrick, x - 3, y - 48 + s);
            }
        }

        // Figuras de la locura: siluetas oscuras con ojos brillantes que flotan hacia ti
        void DibujarAlucinaciones(Graphics g)
        {
            foreach (Alucinacion a in alucinaciones)
            {
                float x = a.Pos.X, y = a.Pos.Y;
                float pulso = 0.5f + 0.5f * (float)Math.Sin(tiempo * 6 + a.Fase);
                float vida = Math.Max(0f, Math.Min(1f, a.Vida));
                int alfa = (int)((80 + 110 * pulso) * vida);
                float temblor = (float)Math.Sin(tiempo * 17 + a.Fase) * 2.2f;

                // aura
                using (var aura = new SolidBrush(Color.FromArgb(Math.Max(8, alfa / 3), 90, 0, 130)))
                    g.FillEllipse(aura, x - 36, y - 54, 72, 100);

                using var cuerpo = new SolidBrush(Color.FromArgb(alfa, 6, 0, 14));
                using var brazo = new Pen(Color.FromArgb(alfa, 12, 0, 24), 3f);

                g.FillEllipse(cuerpo, x - 16 + temblor, y - 58, 32, 86);       // torso alargado
                g.FillEllipse(cuerpo, x - 11 + temblor, y - 72, 22, 26);       // cabeza
                g.DrawLine(brazo, x - 13, y - 40, x - 36 - pulso * 7, y - 4 + temblor);
                g.DrawLine(brazo, x + 13, y - 40, x + 36 + pulso * 7, y - 4 - temblor);
                g.DrawLine(brazo, x - 6, y + 20, x - 12 - pulso * 3, y + 48);
                g.DrawLine(brazo, x + 6, y + 20, x + 12 + pulso * 3, y + 48);

                // ojos que brillan en la oscuridad
                int ojo = (int)((150 + 100 * pulso) * vida);
                using var brillo = new SolidBrush(Color.FromArgb(Math.Max(0, ojo), 255, 210, 255));
                g.FillEllipse(brillo, x - 8 + temblor, y - 64, 5.5f, 4f);
                g.FillEllipse(brillo, x + 2.5f + temblor, y - 64, 5.5f, 4f);

                // boca abierta
                using var boca = new Pen(Color.FromArgb(alfa, 170, 0, 25), 2f);
                g.DrawArc(boca, x - 6 + temblor, y - 53, 12, 8, 0, 180);
            }
        }

        // Don "Ojo del Ser": resalta a las personas a través de paredes y oscuridad
        void DibujarMarcasOjoDelSer(Graphics g)
        {
            if (!ojoDelSer) return;

            foreach (Npc n in cuarto.Npcs)
            {
                if (n.Cortado || n.Tipo == "comido") continue;
                float x = n.Pos.X, y = n.Pos.Y;
                int a = (int)(70 + 50 * Math.Sin(tiempo * 4 + x * 0.05f));
                using var relleno = new SolidBrush(Color.FromArgb(Math.Max(15, a / 3), 200, 20, 30));
                using var anillo = new Pen(Color.FromArgb(Math.Max(60, a + 60), 220, 40, 40), 1.6f);
                g.FillEllipse(relleno, x - 18, y - 46, 36, 72);
                g.DrawEllipse(anillo, x - 14, y - 30, 28, 56);
            }
        }

        void DibujarEnfermera(Graphics g)
        {
            float cx = (float)Math.Cos(angulo), cy = (float)Math.Sin(angulo);
            DibujarSombra(g, px, py + 13, 30);

            g.FillEllipse(brPeloEnf, px - cx * 9 - 4.5f, py - cy * 9 - 4.5f, 9, 9);                 // moño de pelo
            var cuerpo = new RectangleF(px - 11, py - 11, 22, 22);
            using (var uni = new LinearGradientBrush(cuerpo, Color.White, Color.FromArgb(185, 198, 215), 90f))
                g.FillEllipse(uni, cuerpo);                                                         // uniforme
            g.DrawEllipse(penRibete, cuerpo);
            g.FillEllipse(brPiel, px - 6, py - 6, 12, 12);                                          // cabeza
            g.FillEllipse(Brushes.White, px - 7, py - 8, 14, 7);                                    // cofia con cruz roja
            g.FillRectangle(Brushes.Crimson, px - 3, py - 6, 6, 1.6f);
            g.FillRectangle(Brushes.Crimson, px - 0.8f, py - 8, 1.6f, 5.2f);

            float fx = px + cx * 14, fy = py + cy * 14;
            g.FillEllipse(brPiel, px + cx * 10 - 3, py + cy * 10 - 3, 6, 6);                        // mano al frente
            if (mano != null) DibujarObjeto(g, mano, fx, fy);                                       // el objeto que lleva en la mano
            else g.FillEllipse(Brushes.Yellow, fx - 3, fy - 3, 6, 6);

            // El filo manchado de sangre: gotas sobre el arma y cayendo al suelo
            if (armaSangrienta && mano != null)
            {
                g.FillEllipse(brSangre, fx - 5, fy - 4, 11, 8);
                g.FillEllipse(Brushes.DarkRed, fx + 1, fy + 1, 4, 4);
                using var gota = new Pen(Color.FromArgb(190, 120, 10, 10), 1.5f);
                g.DrawLine(gota, fx + 3, fy + 5, fx + 3, fy + 10);
                g.FillEllipse(Brushes.DarkRed, fx + 1.5f, fy + 9, 3, 3);
            }
        }

        // Capas de oscuridad: la linterna es un cono con rayos que chocan con las paredes
        void DibujarOscuridad(Graphics g)
        {
            float[] rangos = { 340, 250, 160 };
            float[] aperturas = { 52, 38, 26 };
            float[] radios = { 95, 70, 48 };
            bool luzOn = linterna && parpadeo <= 0;
            float f = luzOn ? 1f : 0.5f;

            for (int i = 0; i < 3; i++)
            {
                using var circulo = PoligonoLuz(360, radios[i] * f, 40);
                using var luz = new Region(circulo);
                if (luzOn)
                {
                    using var cono = PoligonoLuz(aperturas[i], rangos[i], 40);
                    luz.Union(cono);
                }
                foreach (var (pos, radio) in cuarto.Luces)   // lámparas de la habitación
                {
                    float rr = radio * (1f - i * 0.25f) * LuzTitilante();
                    using var lampara = new GraphicsPath();
                    lampara.AddEllipse(pos.X - rr, pos.Y - rr, rr * 2, rr * 2);
                    luz.Union(lampara);
                }
                g.ExcludeClip(luz);
                g.FillRectangle(brOscuro, 0, 0, W * T, H * T);
                g.ResetClip();
            }
        }

        GraphicsPath PoligonoLuz(float apertura, float rango, int rayos)
        {
            var pts = new List<PointF>();
            bool completo = apertura >= 360;
            if (!completo) pts.Add(new PointF(px, py));
            float inicio = completo ? 0 : angulo - Rad(apertura) / 2;
            float paso = Rad(apertura) / rayos;

            for (int i = 0; i <= rayos; i++)
            {
                float a = inicio + i * paso;
                float cx = (float)Math.Cos(a), cy = (float)Math.Sin(a);
                float d = 0;
                while (d < rango && !EsOpaco(px + cx * d, py + cy * d)) d += 4;
                d = Math.Min(d + 14, rango);   // ilumina un poco la cara de la pared
                pts.Add(new PointF(px + cx * d, py + cy * d));
            }

            var path = new GraphicsPath();
            path.AddPolygon(pts.ToArray());
            return path;
        }

        // Cosas que se ven en la oscuridad: el brillo de los objetos
        void DibujarBrillos(Graphics g)
        {
            foreach (Objeto o in cuarto.Objetos)
            {
                if (Dist(px, py, o.Pos.X, o.Pos.Y) < 250 && VisionLibre(px, py, o.Pos.X, o.Pos.Y))
                {
                    int alfa = (int)(90 + 80 * Math.Sin(tiempo * 4));
                    using var brillo = new SolidBrush(Color.FromArgb(alfa, 200, 240, 255));
                    g.FillEllipse(brillo, o.Pos.X - 5, o.Pos.Y - 5, 10, 10);
                }
            }
        }

        void DibujarHUD(Graphics g)
        {
            using var fuente = new Font("Consolas", 13, FontStyle.Bold);
            using var pequena = new Font("Consolas", 10);
            using var panel = new SolidBrush(Color.FromArgb(115, 0, 0, 0));

            g.FillRectangle(panel, 6, 4, 430, 176);
            g.DrawRectangle(penPanel, 6, 4, 430, 176);
            g.DrawString("Mano: " + (mano ?? "vacía") + (armaSangrienta ? "  (con sangre)" : ""), fuente,
                armaSangrienta ? Brushes.Firebrick : Brushes.Gold, 12, 8);

            // Barra de estamina (crece con la carta "más estamina")
            g.FillRectangle(Brushes.DimGray, 12, 32, 150, 10);
            g.FillRectangle(cansado ? Brushes.Firebrick : Brushes.LimeGreen, 12, 32,
                150 * Math.Max(0f, Math.Min(1f, stamina / staminaMax)), 10);
            g.DrawRectangle(penPanel, 12, 32, 150, 10);
            g.DrawString("STAMINA", pequena, Brushes.Gray, 168, 30);

            // Barra de cordura
            Brush colorCordura = cordura > 60f ? Brushes.MediumPurple : cordura > CorduraCritica ? Brushes.OrangeRed : Brushes.Red;
            g.FillRectangle(Brushes.DimGray, 12, 50, 150, 10);
            g.FillRectangle(colorCordura, 12, 50, 150 * Math.Max(0f, Math.Min(1f, cordura / CorduraMax)), 10);
            g.DrawRectangle(penPanel, 12, 50, 150, 10);
            g.DrawString($"CORDURA: {(int)cordura}%{(cordura <= CorduraCritica ? "  ¡CRÍTICO!" : "")}", pequena, colorCordura, 168, 48);
            if (partesServidas > 0)
                g.DrawString($"Locura x{FactorLocura():0.0}", pequena, Brushes.MediumPurple, 168, 64);

            g.DrawString(linterna ? "Linterna: ON" : "Linterna: OFF", fuente, Brushes.Gray, 12, 68);
            g.DrawString("Alertas", fuente, alertados >= AlertasMax ? Brushes.Firebrick : Brushes.Gray, 12, 86);

            // Una casilla por persona alertada (máximo 10)
            for (int i = 0; i < AlertasMax; i++)
            {
                g.FillRectangle(i < alertados ? Brushes.Firebrick : Brushes.DimGray, 122 + i * 14, 89, 10, 10);
                g.DrawRectangle(penPanel, 122 + i * 14, 89, 10, 10);
            }

            if (actual == 2)
                g.DrawString(llaveSoltada ? $"Partes dadas al ser: {partesServidas}  (ya te dio la llave)" : $"Partes dadas al ser: {partesServidas}/10",
                    fuente, llaveSoltada ? Brushes.LimeGreen : Brushes.OrangeRed, 12, 104);
            int acompanantes = cuarto.Npcs.FindAll(x => x.Sigue && !x.Cortado).Count;
            if (acompanantes > 0) g.DrawString($"Te sigue: {acompanantes}", fuente, Brushes.Gold, 12, 122);
            if (enloquecido) g.DrawString("¡ENLOQUECIDO! Busca pastillas azules", fuente, Brushes.MediumPurple, 12, 136);
            if (alucinaciones.Count > 0)
                g.DrawString($"¡ALUCINACIONES! ({alucinaciones.Count}) No dejes que te toquen", fuente, Brushes.MediumPurple, 12, 154);

            if (policiaLlamada && estado == Estado.Jugando)
            {
                string aviso = policiaLlego ? "¡LA POLICÍA TE BUSCA!" : $"Policía llegando en {Math.Max(0, (int)Math.Ceiling(policiaTimer))} s";
                Texto(g, aviso, 20, (int)(tiempo * 4) % 2 == 0 ? Color.Red : Color.DodgerBlue, 8);
            }

            // Temporizador del caníbal: hay que darle una parte antes de que se acabe
            if (hambriento && estado == Estado.Jugando)
            {
                var caja = new Rectangle(W * T - 290, 8, 280, 52);
                g.FillRectangle(panel, caja);
                g.DrawRectangle(penPanel, caja);
                if (canibalLibre)
                {
                    g.DrawString("¡EL SER ESTÁ LIBRE!", fuente, (int)(tiempo * 6) % 2 == 0 ? Brushes.Red : Brushes.DarkRed, caja.X + 10, caja.Y + 6);
                    g.DrawString("Dale una parte para calmarlo", pequena, Brushes.Gray, caja.X + 10, caja.Y + 30);
                }
                else
                {
                    float frac = Math.Max(0f, Math.Min(1f, hambre / (HambreMax() + extraTiempoHambre)));
                    Brush col = frac > 0.5f ? Brushes.Goldenrod : frac > 0.25f ? Brushes.OrangeRed : Brushes.Red;
                    g.DrawString($"HAMBRE DEL SER: {(int)Math.Ceiling(Math.Max(0f, hambre))} s", fuente, col, caja.X + 10, caja.Y + 6);
                    g.FillRectangle(Brushes.DimGray, caja.X + 10, caja.Y + 32, 260, 10);
                    g.FillRectangle(col, caja.X + 10, caja.Y + 32, 260 * frac, 10);
                    g.DrawRectangle(penPanel, caja.X + 10, caja.Y + 32, 260, 10);
                }
            }

            g.DrawString("E: interactuar   X: usar filo   Q: soltar   F: linterna   Shift: correr", pequena, Brushes.Gray, 12, H * T - 22);

            if (estado != Estado.Jugando) return;
            if (prompt != "" && hablando == null) Texto(g, prompt, 16, Color.Gold, H * T - 100);
            if (tiempoMensaje > 0) Texto(g, mensaje, 15, Color.White, H * T - 66);
        }

        // ================= PANTALLA DE CARTAS =================
        void DibujarCartas(Graphics g)
        {
            using (var velo = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
                g.FillRectangle(velo, 0, 0, W * T, H * T);

            Texto(g, "EL SER TE CONCEDE UN DON", 30, Color.DarkRed, 38);
            Texto(g, "Elige una carta con las teclas 1, 2 o 3. El mundo se detiene mientras decides.", 15, Color.Gray, 92);
            Texto(g, $"Partes dadas al ser: {partesServidas}", 14, Color.OrangeRed, 118);

            int[] xs = { 30, 350, 670 };
            for (int i = 0; i < cartasActuales.Count && i < 3; i++)
            {
                var r = new Rectangle(xs[i], 160, 260, 350);
                using (var fondo = new LinearGradientBrush(r, Color.FromArgb(245, 28, 8, 8), Color.FromArgb(245, 8, 4, 4), 90f))
                    g.FillRectangle(fondo, r);
                g.DrawRectangle(Pens.DarkRed, r);
                g.DrawRectangle(penPanel, r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);

                using (var fTitulo = new Font("Georgia", 16, FontStyle.Bold))
                using (var sfTitulo = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
                    g.DrawString(NombresCarta[cartasActuales[i]], fTitulo, Brushes.Gold,
                        new RectangleF(r.X + 14, r.Y + 22, r.Width - 28, 70), sfTitulo);

                using (var fTexto = new Font("Georgia", 12.5f))
                using (var sfTexto = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.Word })
                    g.DrawString(DescrCarta[cartasActuales[i]], fTexto, Brushes.WhiteSmoke,
                        new RectangleF(r.X + 18, r.Y + 105, r.Width - 36, 190), sfTexto);

                g.FillEllipse(Brushes.DarkRed, r.X + r.Width / 2 - 18, r.Bottom - 58, 36, 36);
                g.DrawEllipse(Pens.Gold, r.X + r.Width / 2 - 18, r.Bottom - 58, 36, 36);
                using (var fNum = new Font("Georgia", 18, FontStyle.Bold))
                using (var sfNum = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString((i + 1).ToString(), fNum, Brushes.White,
                        new RectangleF(r.X + r.Width / 2 - 18, r.Bottom - 58, 36, 36), sfNum);
            }
        }

        void DibujarTitulo(Graphics g)
        {
            // fondo rojo oscuro con goteras de sangre
            using (var fondo = new LinearGradientBrush(new Rectangle(0, 0, W * T, H * T), Color.FromArgb(38, 0, 0), Color.FromArgb(4, 0, 0), 90f))
                g.FillRectangle(fondo, 0, 0, W * T, H * T);
            var rr = new Random(11);
            for (int i = 0; i < 26; i++)
            {
                float gx = 20 + i * 37 + rr.Next(-8, 9);
                float largo = 20 + (float)((Math.Sin(i * 1.7) + 1) * 40) + rr.Next(0, 30) + (float)Math.Sin(tiempo * 0.8 + i) * 4;
                using var pen = new Pen(Color.FromArgb(190, 120, 8, 8), 3 + rr.Next(0, 3));
                g.DrawLine(pen, gx, 0, gx, largo);
                g.FillEllipse(brSangre, gx - 4, largo - 2, 8, 9);
            }
            g.DrawImageUnscaled(Vineta, 0, 0);

            int pulso = (int)(90 + 50 * Math.Sin(tiempo * 2.5));
            Color brillo = Color.FromArgb(Math.Max(20, pulso / 2), 230, 0, 0);   // resplandor que late detrás del título
            Texto(g, "TURNO DE NOCHE", 52, brillo, 59);
            Texto(g, "TURNO DE NOCHE", 52, brillo, 69);
            Texto(g, "TURNO DE NOCHE", 52, Color.DarkRed, 64);
            Texto(g, "Eres enfermera. El hospital está vacío... casi.", 16, Color.Gray, 140);
            Texto(g, "WASD / Flechas: moverte      Shift: correr      F o clic: linterna", 16, Color.Silver, 180);
            Texto(g, "E: interactuar      X: cortar con lo que lleves en la mano      Q: soltar", 16, Color.Silver, 210);
            Texto(g, "Acércate a alguien y pulsa E: habla con él, atráelo o córtalo.", 16, Color.Gold, 250);
            Texto(g, "Solo puedes llevar un objeto en la mano. Cada habitación es una escena.", 16, Color.Gold, 280);
            Texto(g, "Si te ven cortar a alguien, se alertan (una alerta por persona). A las 10 llaman a la policía.", 16, Color.Firebrick, 320);
            Texto(g, "En el quirófano vive un caníbal. Al darle 2 partes se intranquiliza:", 16, Color.OrangeRed, 360);
            Texto(g, "aliméntalo a tiempo o se levantará, cazará a la gente y irá por ti.", 16, Color.OrangeRed, 389);
            Texto(g, "Con 10 partes te da la llave del exterior.", 16, Color.OrangeRed, 418);
            Texto(g, "Cada 5 partes: 3 cartas entre 18 dones. La cordura baja y las pastillas escasean.", 15, Color.Silver, 446);
            Texto(g, "Con la cordura crítica aparecen alucinaciones: si te tocan, pierdes la partida.", 15, Color.MediumPurple, 470);
            Texto(g, "Los pacientes y visitantes no aparecen hasta que te encuentres con el caníbal.", 15, Color.Silver, 494);
            Texto(g, "Escribe tu nombre y pulsa ENTER:", 16, Color.Gray, 518);
            string nombreMostrado = string.IsNullOrWhiteSpace(nombreJugador) ? "Enfermera" : nombreJugador;
            Texto(g, "► " + nombreMostrado + (((int)(tiempo * 2)) % 2 == 0 ? "_" : "") + " ◄", 24, Color.White, 542);
            Texto(g, "Pulsa ENTER para empezar", 22, Color.White, 582);
        }

        void Texto(Graphics g, string texto, float tamano, Color color, float y)
        {
            using var fuente = new Font("Georgia", tamano, FontStyle.Bold);
            using var pincel = new SolidBrush(color);
            using var formato = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(texto, fuente, pincel, new RectangleF(0, y, W * T, tamano * 3), formato);
        }
    }
}