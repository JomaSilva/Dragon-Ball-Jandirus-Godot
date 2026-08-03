using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ROBO DE TESTE (`--socar`). Soca sem parar e narra o que o servidor devolve.
///
/// Existe pelo mesmo motivo do `--treinar` e do `--diagvisual`: sem janela ninguem aperta
/// tecla, e a cadeia do combate -- pacote de golpe, escolha de alvo, resolucao, transmissao,
/// relato -- so se prova de ponta a ponta com alguem batendo de verdade. Dois processos com
/// esta flag no mesmo servidor produzem uma briga completa em texto.
///
/// Nao entra em jogo normal: sem a flag, este no nunca e criado.
/// </summary>
public partial class RoboDeSoco : Node
{
    private double _proximo;
    private double _cadencia = Protocol.AttackPoseMs / 1000.0;
    private int _golpes, _acertos, _aparados, _contras, _erros, _esquivas;
    private double _relatorio = 5;

    public override void _Ready()
    {
        if (GameClient.Instance is not { } cli) return;
        cli.Golpe += AoGolpe;
        cli.SheetUpdated += f => { if (f.SocoMs > 0) _cadencia = f.SocoMs / 1000.0; };
        // luta pra valer: o teste precisa chegar a quebra de membro e a morte
        cli.SendLethal(true);
        cli.Falou += Ouviu;

        // MARCA O PRIMEIRO QUE APARECER. Sem isto os dois robos vao e voltam em fase trocada e
        // podem passar o teste inteiro sem se encontrar -- ja aconteceu, e o resultado foi um
        // relatorio de 74 golpes e ZERO acertos que parecia regressao de combate e nao era.
        // Com alvo marcado a investida vai atras dele, entao a briga acontece de verdade.
        cli.SnapshotReceived += Avistou;
        cli.SkillsMudaram += () => GD.Print($"[robo] skills: {cli.SkillsAprendidas.Count} | marcos {cli.MarcosLivres}/{cli.MarcosTotais}");
        cli.FormaMudou += (quem, de, para, primeira) =>
        {
            if (quem != cli.LocalId) return;
            GD.Print($"[robo] forma {de} -> {para}{(primeira ? "  (PRIMEIRA VEZ: cinematica)" : "")}"
                     + $" | BP expresso {cli.Sheet.ExpressedBP:N0}");
        };
        GD.Print("[robo] socando -- alvo: quem estiver na frente");
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance is not { } cli) return;
        cli.Golpe -= AoGolpe;
        cli.Falou -= Ouviu;
        cli.SnapshotReceived -= Avistou;
    }

    private void Avistou(List<Jandirus.Net.EntityState> estados)
    {
        if (GameClient.Instance is not { } cli) return;

        foreach (Jandirus.Net.EntityState e in estados)
        {
            if (e.Id == cli.LocalId) continue;
            if (cli.AlvoId == 0)
            {
                cli.SendAlvo(e.Id);
                GD.Print($"[robo] alvo marcado: id {e.Id}");
            }
            if (e.Id == cli.AlvoId) _alvoPos = new Vector2(e.Pos.X, e.Pos.Y);
        }
    }

    // =====================================================================
    // O ROBO TAMBEM FALA
    // =====================================================================
    /// <summary>
    /// Uma frase a cada tantos segundos, alternando os canais.
    ///
    /// Nao e enfeite: dois `--socar` no mesmo servidor sao o unico teste automatico que existe
    /// com DUAS pontas, e o chat so se prova com duas -- alcance por distancia, sussurro que
    /// vira teaser de longe, OOC que atravessa planeta. Sem isso, a unica forma de saber que a
    /// fala chega seria alguem digitar.
    ///
    /// Eles se afastam e se aproximam o tempo todo (ver <see cref="Andar"/>), entao as duas
    /// pontas do alcance sao percorridas sozinhas ao longo do teste.
    /// </summary>
    private void Tagarelar()
    {
        if (GameClient.Instance is not { } cli) return;
        Protocol.Fala canal = (_frase % 4) switch
        {
            0 => Protocol.Fala.Diz,
            1 => Protocol.Fala.Sussurro,
            2 => Protocol.Fala.Emote,
            _ => Protocol.Fala.Ooc,
        };
        cli.SendChat(canal, $"teste de {canal} numero {_frase}");
        _frase++;
    }

    /// <summary>
    /// APRENDE A PRIMEIRA SKILL QUE COUBER NO BOLSO.
    ///
    /// Fecha a ultima ponta que faltava provar sozinha: catalogo lido -> oferta calculada com a
    /// raca certa -> pedido -> validacao no servidor -> lista de volta -> gravacao no save. Sem
    /// isto, so digitando no menu daria pra saber se a compra funciona.
    /// </summary>
    private void Estudar()
    {
        // ESTUDA ENQUANTO TIVER MARCO, nao uma vez so. Comprar uma skill provava a compra; o que
        // falta provar e a TECNICA chegando no botao, e a skill que destrava tecnica custa 2 --
        // parando na primeira compra o teste nunca chegava la.
        if (GameClient.Instance is not { } cli || cli.MarcosLivres <= 0) return;

        var cat = MenuJogo.CatalogoPublico();
        if (cat == null) return;

        var livro = new Jandirus.Core.Skills.SkillBook { MarcosLivres = cli.MarcosLivres };
        livro.Carregar(cli.SkillsAprendidas);

        var ofertas = livro.Ofertas(cat, cli.Atributos.Raca ?? "", cli.Sheet.Class ?? "", false)
                           .Where(o => o.Item2 == Jandirus.Core.Skills.Recusa.Pode)
                           .Select(o => o.Item1).ToList();
        if (ofertas.Count == 0) return;

        // PREFERE UMA QUE DESTRAVE TECNICA JA PORTADA. Aprender qualquer coisa provava a compra;
        // provar que a tecnica CHEGA no botao e dispara exige comprar a skill certa, e o robo e
        // quem tem que escolher -- num teste automatico ninguem esta olhando o menu.
        Jandirus.Core.Skills.Skill escolhida =
            ofertas.FirstOrDefault(s2 => s2.Estilo.Length > 0)
            ?? ofertas.FirstOrDefault(s2 => s2.Verbos.Any(v =>
                Jandirus.Core.Skills.Tecnicas.Get(v) is { Modo: not Jandirus.Core.Skills.Modo.NaoPortada }))
            // o que soma em `bodyskill`/`bodyreadiness` vem ANTES do buff comum: sao esses que
            // destravam arvore, e sem eles o teste nunca chega na Martial Arts nem num estilo
            ?? ofertas.FirstOrDefault(s2 => s2.Buffs.ContainsKey("bodyskill") || s2.Buffs.ContainsKey("bodyreadiness"))
            ?? ofertas.FirstOrDefault(s2 => s2.Buffs.Count > 0)
            ?? ofertas[0];

        GD.Print($"[robo] tentando aprender '{escolhida.Nome}' ({escolhida.Path})"
                 + (escolhida.Verbos.Length > 0 ? $" -- destrava {string.Join(", ", escolhida.Verbos)}" : "")
                 + (escolhida.Estilo.Length > 0 ? $" -- ESTILO {escolhida.Estilo}" : "")
                 + (escolhida.Buffs.Count > 0 ? $" -- buffs {string.Join(", ", escolhida.Buffs.Select(kv => $"{kv.Key}+{kv.Value:0.##}"))}" : ""));
        cli.SendAprender(escolhida.Path);
    }

    /// <summary>
    /// DISPARA AS TECNICAS ATIVAS que ja tem efeito, uma por vez.
    ///
    /// Exercita os tres encanamentos diferentes de uma vez so: cegar alguem (visao alheia),
    /// sumir (o bit do snapshot) e erguer escudo (cadeia de stats). Sem isto o unico jeito de
    /// saber que o canal de habilidade aceita tecnica de skill seria clicando no menu.
    /// </summary>
    private void Tecnicar()
    {
        if (GameClient.Instance is not { } cli) return;
        var cat = MenuJogo.CatalogoPublico();
        if (cat == null) return;

        var tenho = new List<string>();
        foreach (string path in cli.SkillsAprendidas)
            if (cat.Get(path) is { } s2)
                foreach (string v in s2.Verbos)
                    if (Jandirus.Core.Skills.Tecnicas.Get(v) is { Modo: not Jandirus.Core.Skills.Modo.NaoPortada }
                        && !tenho.Contains(v)) tenho.Add(v);

        if (tenho.Count == 0) return;
        string id = tenho[_tecnica++ % tenho.Count];
        GD.Print($"[robo] usando tecnica {id}");
        cli.SendHabilidade(id);
    }

    private int _tecnica;
    private double _proximaTecnica = 14;

    /// <summary>
    /// A ROTINA DE TECNOLOGIA, em ordem: ergue -> aparafusa -> estuda -> ergue o mainframe ->
    /// instala o laboratorio -> vira androide.
    ///
    /// UM PASSO POR VEZ E NA ORDEM porque cada um depende do anterior no servidor -- e e
    /// justamente essa cadeia que precisa de prova. Mandar tudo de uma vez testaria so o
    /// despacho; assim testa a MAQUINA DE ESTADOS: instalar laboratorio sem aparafusar
    /// (o passo 8) TEM que ser recusado, e o teste passa por ele de proposito.
    /// </summary>
    private void PassoDeTech()
    {
        if (GameClient.Instance is not { } cli) return;
        GD.Print($"[robo] tech: passo {_passoTech} (nivel {cli.TechNivel:0}, {cli.Zeni:N0} zeni, {cli.Obras.Count} obras na zona)");
        switch (_passoTech++)
        {
            case 0: cli.SendTech("lista"); break;
            case 1: cli.SendTech("construir", "Research_Station"); break;
            case 2: cli.SendTech("aparafusar"); break;
            case 3: cli.SendTech("estudar"); break;
            case 4: break;                            // deixa estudar um pouco
            case 5: cli.SendTech("estudar"); break;   // para
            // ANDA UM POUCO ANTES DO PROXIMO: construcao nao empilha no mesmo ponto, e o teste
            // tem que passar pela regra em vez de contorna-la.
            // SAI DO PONTO ANTES DE CONSTRUIR DE NOVO. O robo so anda quando tem ALVO
            // (`_alvoPos`), e num servidor vazio nao tem nenhum -- ele ficava parado e a segunda
            // construcao caia em cima da primeira ("ja tem coisa demais neste ponto", que e a
            // regra funcionando). Um destino falso resolve, e passa pelo mesmo movimento de
            // sempre em vez de teleportar.
            case 6:
                if (World.Instancia?.PosicaoLocal is { } aqui) _alvoPos = aqui + new Vector2(160, 0);
                break;
            case 7: case 8: break;   // deixa andar
            case 9: cli.SendTech("construir", "Android_Creation_Mainframe"); break;
            case 10: cli.SendTech("lab_androide"); break;   // sem aparafusar: TEM que recusar
            case 11: cli.SendTech("aparafusar"); break;
            // AS DUAS VIAS SAO EXCLUDENTES, e o proprio jogo diz isso: o mainframe e
            // `allowedRaces = list("Human")`, entao quem VIROU androide nao constroi outro. A
            // primeira versao deste teste fazia as duas na sequencia e levou uma recusa correta
            // ("nao e coisa de Android") -- que e exatamente a regra funcionando. Uma rota por
            // execucao: `--tech` vira androide, `--tech --bio` faz o laboratorio de DNA.
            case 12: cli.SendTech(Bio ? "lab_bio" : "lab_androide"); break;
            case 13:
                if (Bio) cli.SendTech("colher_dna");           // sem ninguem caido: tem que recusar
                else cli.SendTech("androide_absorcao");
                break;
            case 14: if (Bio) cli.SendTech("gestar"); break;   // tanque vazio: tem que recusar
            case 15: cli.SendTech("lista"); break;
            default: _fezTech = true; break;
        }
    }

    private int _passoTech;
    private bool _fezTech;
    private double _proximoPassoTech = 8;

    /// <summary>`--tech`: liga a rotina de tecnologia.</summary>
    public bool TestarTech;

    /// <summary>`--bio`: depois da via do androide, percorre tambem a do bio-androide.</summary>
    public bool Bio;

    private double _proximoEstudo = 6;
    private double _proximaForma = 4;
    private double _proximaRegen = 10;
    /// <summary>Em quantos segundos o robo entra na propria mente. Negativo = nunca (`--mente N`).</summary>
    public double EntrarNaMenteEm { get => _proximaMente; set => _proximaMente = value; }
    private double _proximaMente = -1;
    private bool _meditou;

    /// <summary>Em quantos segundos o robo decola (`--espaco N`). Negativo = ja decolou.</summary>
    public double DecolarEm;
    private double _proximoRumo;

    private void Ouviu(Protocol.Fala canal, string autor, string texto) =>
        GD.Print($"[robo] ouvi ({canal}) {autor}: " + (texto.Length > 0 ? texto : "(sussurro sem conteudo)"));

    private int _frase;
    private double _proximaFrase = 3;

    public override void _Process(double delta)
    {
        if (GameClient.Instance is not { Connected: true } cli) return;

        Andar(delta);

        _proximo -= delta;
        if (_proximo <= 0)
        {
            _proximo = _cadencia;
            _golpes++;
            cli.SendAction();
        }

        _proximaFrase -= delta;
        if (_proximaFrase <= 0) { _proximaFrase = 4; Tagarelar(); }

        _proximoEstudo -= delta;
        // UMA COMPRA POR SEGUNDO. As skills que destravam arvore estao atras de cadeias de
        // pre-requisito de meia duzia de degraus; a cada 5 s o teste nem chegava perto do
        // primeiro estilo dentro do tempo de uma rodada.
        if (_proximoEstudo <= 0) { _proximoEstudo = 1; Estudar(); }

        // SOBE A ESCADA DE TEMPOS EM TEMPOS. So tem efeito com BP suficiente (ver --bpteste);
        // sem isso a transformacao seria a unica parte do jogo sem teste automatico.
        _proximaForma -= delta;
        if (_proximaForma <= 0) { _proximaForma = 8; cli.SendTransformar(true); }

        // tenta regenerar de tempos em tempos: so faz efeito em quem regenera e com Ki
        // cheio, mas e o que exercita o canal de habilidade sem ninguem clicar
        _proximaRegen -= delta;
        if (_proximaRegen <= 0) { _proximaRegen = 12; cli.SendHabilidade("regenerar"); }

        _proximaTecnica -= delta;
        if (_proximaTecnica <= 0) { _proximaTecnica = 6; Tecnicar(); }

        if (TestarTech && !_fezTech)
        {
            _proximoPassoTech -= delta;
            if (_proximoPassoTech <= 0) { _proximoPassoTech = 3; PassoDeTech(); }
        }

        // DECOLA E VOA. `--espaco N`: sobe ao espaco depois de N segundos e liga o piloto
        // automatico pro planeta mais proximo -- e o unico jeito de provar a troca de chunk,
        // o snapshot recortado e o pouso por encostar sem ninguem pilotando.
        if (DecolarEm > 0)
        {
            DecolarEm -= delta;
            if (DecolarEm <= 0) { DecolarEm = -1; cli.SendHabilidade("decolar"); GD.Print("[robo] decolando"); }
        }
        else if (DecolarEm < 0 && Jandirus.Core.World.Espaco.EhEspaco(cli.Zone))
        {
            _proximoRumo -= delta;
            if (_proximoRumo <= 0)
            {
                _proximoRumo = 3;
                Vector2? eu = World.Instancia?.PosicaoLocal;
                if (eu != null && cli.Planetas.Count > 0)
                {
                    // PREFERE UM PLANETA GERADO quando ha algum por perto. O mais proximo e
                    // quase sempre a Terra (o robo decola de la), e pousar de volta em casa nao
                    // testa nada -- o caminho novo e o do planeta que nasce da seed.
                    GameClient.PlanetaInfo alvo = cli.Planetas
                        .OrderBy(p => p.Premade ? 1 : 0)
                        .ThenBy(p => (p.Pos - eu.Value).Length()).First();
                    World.Instancia?.Pilotar(alvo.Pos);
                    GD.Print($"[robo] rumo a {alvo.Nome} | {(alvo.Pos - eu.Value).Length():N0} px"
                             + $" | chunk {Jandirus.Core.World.ChunkId.De(new Jandirus.Core.World.Vec2(eu.Value.X, eu.Value.Y))}");
                }
            }
        }

        // ENTRA NA MENTE UMA VEZ, se o robo estiver sozinho no servidor. E o unico jeito de
        // exercitar a IA sem uma segunda pessoa -- e o clone e um oponente que sempre existe.
        if (!_meditou && _proximaMente > 0)
        {
            _proximaMente -= delta;
            if (_proximaMente <= 0)
            {
                _meditou = true;
                cli.SendActivity(Protocol.Activity.Meditando);
                cli.SendHabilidade("mente");
                GD.Print("[robo] entrando na mente");
            }
        }

        _relatorio -= delta;
        if (_relatorio > 0) return;
        _relatorio = 5;
        string corpo = "";
        foreach (Protocol.ParteState p in cli.Corpo)
            if (p.Decepado || p.Vida < 100) corpo += $" {p.Nome}={(p.Decepado ? "X" : p.Vida.ToString())}";

        GD.Print($"[robo] {_golpes} golpes | acertos {_acertos} aparados {_aparados} "
                 + $"contras {_contras} erros {_erros} esquivas {_esquivas} "
                 + $"| vida {cli.Sheet.HP:0.0} | rabo {(cli.Sheet.Rabo ? "sim" : "nao")}"
                 + $" | membros {cli.Corpo.Count}"
                 + (cli.Sheet.Imobilizado ? "  <- NO CHAO" : "")
                 + (corpo.Length > 0 ? $"\n         feridos:{corpo}" : ""));
    }

    /// <summary>
    /// VAI ATRAS DO ALVO. O robo aperta as teclas de verdade (`Input.ActionPress`) em vez de
    /// mexer na posicao: assim o teste passa pelo MESMO caminho do jogador -- movimento local,
    /// bit de corrida no pacote, concessao do servidor, validacao de passo.
    ///
    /// POR QUE PERSEGUIR E NAO OSCILAR. A versao anterior ia e voltava num eixo. Dois robos
    /// com velocidades diferentes (racas diferentes) e subindo com segundos de diferenca
    /// entravam em FASE TROCADA e podiam passar o teste inteiro sem se cruzar: o relatorio
    /// saia com dezenas de golpes e ZERO acertos, o que parece regressao de combate e nao e.
    /// Perseguindo o alvo marcado, a briga acontece toda vez.
    ///
    /// Perto demais o robo PARA de andar: o arranque do soco so dispara com distancia pra
    /// fechar, e colado ele viraria uma dancinha em cima do outro.
    /// </summary>
    private void Andar(double delta)
    {
        Vector2? eu = World.Instancia?.PosicaoLocal;
        if (eu == null || _alvoPos == null) { Parar(); return; }

        Vector2 d = _alvoPos.Value - eu.Value;
        if (d.Length() < 40f) { Parar(); return; }   // ja esta no alcance: para e soca

        Segurar("move_right", d.X > 8f);
        Segurar("move_left", d.X < -8f);
        Segurar("move_down", d.Y > 8f);
        Segurar("move_up", d.Y < -8f);
        Godot.Input.ActionPress("run");
    }

    private void Parar()
    {
        foreach (string a in Direcoes) Godot.Input.ActionRelease(a);
        Godot.Input.ActionRelease("run");
    }

    private static void Segurar(string acao, bool sim)
    {
        if (sim) Godot.Input.ActionPress(acao);
        else Godot.Input.ActionRelease(acao);
    }

    private static readonly string[] Direcoes = ["move_right", "move_left", "move_up", "move_down"];

    /// <summary>Onde o alvo estava no ultimo snapshot.</summary>
    private Vector2? _alvoPos;

    private void AoGolpe(Protocol.HitEvent h)
    {
        if (GameClient.Instance is not { } cli) return;
        bool bati = h.Atacante == cli.LocalId;

        switch ((Jandirus.Core.Combat.Desfecho)h.Desfecho)
        {
            case Jandirus.Core.Combat.Desfecho.Acertou:
            case Jandirus.Core.Combat.Desfecho.Critico: if (bati) _acertos++; break;
            case Jandirus.Core.Combat.Desfecho.Aparou: if (bati) _aparados++; break;
            case Jandirus.Core.Combat.Desfecho.Contra: if (bati) _contras++; break;
            case Jandirus.Core.Combat.Desfecho.Errou: if (bati) _erros++; break;
            case Jandirus.Core.Combat.Desfecho.Esquivou: if (bati) _esquivas++; break;
        }

        // so os acontecimentos que importam viram linha: a cada 0,3 s um relato encheria o log
        if (!h.Quebrou && !h.Decepou && !h.Nocauteou && !h.Morreu) return;
        string quem = bati ? "ELE" : "EU";
        GD.Print($"[robo] {quem}: {h.Membro}"
                 + (h.Decepou ? " ARRANCADO" : h.Quebrou ? " quebrado" : "")
                 + (h.Morreu ? " -- MORTE" : h.Nocauteou ? " -- NOCAUTE" : ""));
    }
}
