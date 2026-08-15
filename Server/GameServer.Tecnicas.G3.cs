using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// LOTE G3 -- AREA, MULTI-ALVO E CORPO-A-CORPO ESPECIAL.
///
/// Sete tecnicas que tem uma coisa em comum e e por isso que vieram juntas: NENHUMA delas e um
/// golpe unico e instantaneo. Ou pegam VARIAS pessoas de uma vez (Sword Strike, Shield Bash,
/// Final Explosion), ou pegam a MESMA pessoa varias vezes (Special Multihit, Light Buster), ou
/// dependem de um estado guardado entre dois apertos do botao (Final Explosion, Rock Paper
/// Scissors). As tres formas exigem coisas que o lote de tecnicas instantaneas nao precisava.
///
/// O QUE PRECISOU SER INVENTADO DE ENCANAMENTO (e por que):
///
///  1. UM PULSO PROPRIO. O BYOND resolvia "bata 10 vezes com 0,2s entre uma e outra" com
///     `sleep(2)` dentro do verb, porque la todo proc e uma corrotina. Aqui o servidor e um laco
///     de tick e nada dorme. Os ganchos de tick que existem (`TickDasTecnicas`, `TickDosBuffs`)
///     moram em arquivos de outros lotes, e este arquivo nao pode toca-los -- entao o lote traz o
///     proprio relogio de 10 Hz, feito de <see cref="SceneTreeTimer"/> encadeado. Ele so existe
///     enquanto ha barragem, carga ou teleporte no ar; com a agenda vazia ele se desliga sozinho
///     e o custo volta a ser zero.
///
///  2. TRAVA DE MOVIMENTO SEM `move=0`. O `Final_Explosion` prende quem carrega. O port valida
///     movimento em `Input()` (outro arquivo), entao a trava aqui e por ANCORA: a posicao de
///     quem carrega e regravada e devolvida por `Correction` a cada pulso. O efeito pro jogador
///     e o mesmo -- ele nao sai do lugar.
///
///  3. DANO ESPALHADO. O `SpreadDamage` do original bate o MESMO valor em cada membro atingivel,
///     em vez de sortear um. E o que faz uma explosao ser diferente de um soco, e o port nao
///     tinha equivalente -- ver <see cref="EspalharDanoG3"/>.
///
/// O RESTO PASSA PELO FUNIL NORMAL (<see cref="MeleeResolver.Resolver"/>). Golpe de tecnica que
/// nao passa por ele e golpe que ignora guarda, esquiva, membro atingido e Zenkai -- ou seja,
/// meio combate. As tecnicas daqui so somam dano plano (`addeddamage` do `doAttack`) e deixam o
/// resto da cadeia intacto.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// NUMEROS DO DM
	// =====================================================================
	/// <summary>
	/// `basicCD += 15` -- 15 decimos de segundo (`Cultivation.dm:287`, `:407`,
	/// `Martial Skill Attacks.dm:126`).
	///
	/// E UM SO PRAS TRES, e isso e do original: `basicCD` e uma variavel do mob, nao da tecnica.
	/// Usar Sword Strike deixa Shield Bash e Special Multihit em espera tambem. Dar uma recarga
	/// separada pra cada uma faria as tres serem usadas em sequencia -- e ai o teto de dano por
	/// segundo do jogo triplicaria de graca.
	/// </summary>
	private const long BasicCdG3 = 1500;

	/// <summary>
	/// `KiSwordDrain` e `KiBladeDrain`, ambos 1 por padrao (`Cultivation.dm:207` e `:88`).
	///
	/// No original esses dois CAEM (pra 0,5 e depois 0) conforme a skill de espada/lamina sobe de
	/// nivel, o que barateia o custo do Sword Strike junto. Aqui ficam no valor de estreia -- que e
	/// o mais caro.
	///
	/// ============================ O MOTOR DE NIVEIS EXISTE; O DEGRAU DA LAMINA E QUE NAO DIZ NADA ============================
	/// Este texto dizia *"o port nao tem nivel de skill ainda"*, e isso CADUCOU:
	/// `Core/Skills/NiveisDeSkill.cs` e o `Assets/Data/niveis.json` (extraido do `effector()`) estao
	/// no jogo e ja entregam buff, verbo e flag por degrau.
	///
	/// A ESCADA DA LAMINA ESTA LA -- `/datum/skill/Cultivation/Blade_Runner`, degraus 1 e 2, com as
	/// duas frases certas ("drain has been halved" / "eliminated completely"). **So que o efeito
	/// dela nao esta em `buffs` nem em `flags`: esta em `logica`** (`superdrain = 0.5`,
	/// `superdrain = 0`), e `NiveisDeSkill.Aplicar` so consome os dois primeiros. Subir de nivel
	/// hoje imprime a mensagem e nao muda numero nenhum.
	///
	/// ENTAO AS CONSTANTES ABAIXO ESTAO CERTAS PELO MOTIVO ERRADO: elas cravam o dreno de estreia, e
	/// por acaso e isso que o jogo entrega. Fechar de verdade e mover o efeito do `logica` pro
	/// `flags` no extrator (e dar campo ao lutador), nao trocar estas duas linhas.
	/// ==================================================================================================================
	/// </summary>
	private const double DrenoDaEspadaG3 = 1, DrenoDaLaminaG3 = 1;

	/// <summary>Raios do Final Explosion, em tiles (`misc.dm:345-350`).</summary>
	private const int RaioPertoG3 = 3, RaioMedioG3 = 10, RaioMaximoG3 = 25;

	/// <summary>`VampBiteCooldown = 300` segundos (`Vampires.dm:7`).</summary>
	private const long MordidaRecargaMs = 300_000;

	/// <summary>Teto e piso do `ParanormalBPMult` da mordida (`Vampires.dm:5-6`, `:21`).</summary>
	private const double VampMultMax = 3.5, VampMultMin = 1;

	/// <summary>`prob(5)` -- a chance de a mordida render poder permanente (`Vampires.dm:17`).</summary>
	private const double VampProcPct = 5;

	/// <summary>`sleep(5)` antes do teleporte do Light Buster (`yardrat.dm:236`), em ms.</summary>
	private const long BusterAvisoMs = 500;

	/// <summary>`view(4)` do Light Buster e `view(2)` do jokenpo, em pixels.</summary>
	private const float AlcanceBusterG3 = 4 * ZoneCollision.TileSize;
	private const float AlcanceJokenpoG3 = 2 * ZoneCollision.TileSize;

	/// <summary>`delay=2` da barragem: 2 decimos de segundo entre um golpe e o proximo.</summary>
	private const long BarragemPassoMs = 200;

	/// <summary>`get_dist(M,src)>=2` corta a barragem (`attack_proc.dm:143`).</summary>
	private const float BarragemDistanciaMaxG3 = 2 * ZoneCollision.TileSize;

	/// <summary>`spawn(25)` entre dois incrementos do `chargecounter` (`misc.dm:363`).</summary>
	private const long CargaPassoMs = 2500;

	/// <summary>`if(chargecounter>10)` -- acima disto a explosao pode matar quem a usou (`misc.dm:319`).</summary>
	private const int CargaLetalG3 = 10;

	/// <summary>`prob(70)` de morrer com a carga acima do limite (`misc.dm:320`).</summary>
	private const double CargaMortePct = 70;

	// =====================================================================
	// ESTADO DO LOTE
	// =====================================================================
	/// <summary>Recarga compartilhada (o `basicCD`): id do jogador -> quando libera.</summary>
	private readonly Dictionary<int, long> _prontoG3 = [];

	/// <summary>Recarga do bonus da mordida: id -> quando o `prob(5)` volta a valer.</summary>
	private readonly Dictionary<int, long> _mordidaProntaG3 = [];

	/// <summary>A escolha secreta do jokenpo: id -> 1 pedra, 2 papel, 3 tesoura. 0/ausente = sem escolha.</summary>
	private readonly Dictionary<int, int> _jokenpoG3 = [];

	/// <summary>Uma barragem em andamento (Special Multihit).</summary>
	private sealed class BarragemG3
	{
		public int Alvo;
		public int Faltam;
		public double AddDano;
		public long ProximoMs;

		/// <summary>
		/// ============================ OS TRES CAMPOS DA PRESA DO LOBO (lote G6) ============================
		/// Os dois verbos do lobo (`Martial Skill Attacks.dm:153` e `:212`) sao barragens de tres e
		/// quatro golpes -- mesma agenda, mesmo funil, mesma parada por guarda. O que eles tem de
		/// proprio sao tres coisas, e as tres estao ESCRITAS no original:
		///
		///   * o Furacao faz `knockbackon = 0` antes da sequencia e devolve no fim (`:219`, `:245`):
		///     o alvo NAO pode sair voando no meio, porque a sequencia inteira depende de ele
		///     continuar colado;
		///   * o Furacao da um `step(src, get_dir(src,target))` entre os golpes (`:224`): quem bate
		///     AVANCA junto;
		///   * o Punho arremessa no ULTIMO golpe (`kbdur = 4`, `:184`).
		///
		/// Entraram como campos da barragem, e nao como uma segunda agenda, porque uma segunda
		/// agenda seria a segunda resposta pra "como um golpe encadeado acontece" -- e as duas
		/// divergiriam na primeira vez que alguem mexesse na parada por guarda.
		/// ==============================================================================================
		/// </summary>
		public bool SemEmpurrao;

		/// <summary>Quantos pixels avancar em direcao ao alvo antes de cada golpe. Zero = fica parado.</summary>
		public float AvancaPx;

		/// <summary>Tiques de arremesso no ULTIMO golpe da sequencia. Zero = nao arremessa.</summary>
		public int ArremessoNoFim;

		/// <summary>
		/// ============================ A ESCADA DOS COMBOS DE BOXE (lote G7) ============================
		/// As tres combinacoes de boxe (`Physical Skills.dm:66`, `:89`, `:120`) sao barragens tambem --
		/// mesmo funil, mesma parada por guarda, mesma parada por distancia --, mas com DOIS numeros
		/// proprios POR GOLPE, e nao um numero pra sequencia inteira:
		///
		///     One_Two        jab (+0)   sleep(2)   cross (+5)
		///     One_Two_Five   jab (+0)   sleep(1)   cross (+4)   sleep(2)   uppercut (+6)
		///     Two_One_Four   cross(+2)  sleep(2)   jab   (+5)   sleep(1)   uppercut (+7)
		///
		/// O soco cresce ao longo do combo e a cadencia MUDA no meio -- e essa aceleracao e a
		/// assinatura de cada uma delas. Com um `AddDano` unico e um passo fixo as tres viravam a mesma
		/// coisa com nomes diferentes.
		///
		/// Quando <see cref="Escada"/> e nula nada muda: a barragem do Multihit e a do lobo continuam
		/// lendo <see cref="AddDano"/> e <see cref="BarragemPassoMs"/>, como sempre. Uma segunda agenda
		/// so pra o boxe seria a segunda resposta pra "como um golpe encadeado acontece" -- e as duas
		/// divergiriam na primeira vez que alguem mexesse na parada por guarda.
		/// ==============================================================================================
		/// </summary>
		public (double Dano, long AtrasoMs)[]? Escada;

		/// <summary>Em que degrau da <see cref="Escada"/> a sequencia esta.</summary>
		public int EscadaIdx;
	}

	/// <summary>Uma carga de Final Explosion em andamento.</summary>
	private sealed class CargaG3
	{
		public int Raio;             // em tiles: 3, 10 ou 25
		public int Contador = 1;     // o `chargecounter`, comeca em 1 (misc.dm:340)
		public Vec2 Ancora;          // onde o corpo fica preso enquanto carrega
		public long ProximoMs;       // proximo incremento do contador
		public long ComecouMs;       // pras duas narracoes de 2s e 6s
		public int Narrou;           // quantas das duas narracoes ja sairam
	}

	/// <summary>Um Light Buster no ar: gritou, ainda nao teleportou.</summary>
	private sealed class BusterG3
	{
		public int Alvo;
		public long QuandoMs;
	}

	private readonly Dictionary<int, BarragemG3> _barragemG3 = [];
	private readonly Dictionary<int, CargaG3> _cargaG3 = [];
	private readonly Dictionary<int, BusterG3> _busterG3 = [];

	// =====================================================================
	// REGISTRO
	// =====================================================================
	public static void RegistrarTecnicasG3()
	{
		Jandirus.Core.Skills.Tecnicas.Registrar("Sword_Strike", "Sword Strike",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Um giro com a espada de Ki que corta TODOS que estiverem na sua frente de uma vez, "
			+ "somando dano em cada um. So sai com a Ki Sword ligada, e deixa os golpes especiais "
			+ "em espera por um segundo e meio.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Shield_Bash", "Shield Bash",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Avanca com o Ki Shield na frente e empurra todo mundo que estiver no caminho. Exige o "
			+ "escudo de pe -- sem ele não ha o que golpear com. Mesma espera do Sword Strike.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Special_Multihit", "Special: Multihit",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Uma barragem de socos num alvo so: ate dez golpes seguidos, um a cada dois decimos de "
			+ "segundo. Quantos saem depende da sua Técnica. Se o alvo se afastar ou erguer a "
			+ "guarda, a barragem para no meio.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Final_Explosion", "Final Explosion",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Duas fases: primeiro você escolhe o tamanho do estouro e comeca a juntar energia, "
			+ "preso no lugar; depois aperta de novo pra detonar. Quanto mais tempo carregar, mais "
			+ "forte -- e passando de vinte e cinco segundos de carga você provavelmente MORRE junto.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Light_Buster", "Light Buster",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Você grita, some, e meio segundo depois está nas costas do alvo acertando quatro "
			+ "golpes seguidos. Pra quem assiste parece teleporte. Precisa de alguém a ate quatro "
			+ "tiles de distância.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Bite", "Morder",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Crava os dentes em quem estiver colado em você. Alimenta -- mata a fome de uma vez -- "
			+ "e de vez em quando o sangue rende poder que NAO vai embora. Esse ganho tem cinco "
			+ "minutos de espera entre um e outro.");

		Jandirus.Core.Skills.Tecnicas.Registrar("Rock_Paper_Scissors", "Jokenpo",
			Jandirus.Core.Skills.Modo.Instantanea,
			"Você escolhe pedra, papel ou tesoura EM SEGREDO. No aperto seguinte você avanca dois "
			+ "passos e soca: pedra pune quem não está atacando, papel pune quem esta, tesoura pune "
			+ "quem está bloqueando. Acertar a leitura vale dano extra.");

		// AS SUB-ESCOLHAS entram no catalogo pra que `Tecnicas.Get()` nao as sintetize como
		// "nao portada" e o servidor recuse com a frase errada. Elas NAO aparecem no menu do
		// jogador -- o menu e montado a partir dos verbs que as skills concedem
		// (`TecnicasDe`), e nenhuma skill concede "Rock_Paper_Scissors_1".
		// AS SUB-ESCOLHAS (Rock_Paper_Scissors_1..3, Final_Explosion_3/10/25) NAO SAO
		// REGISTRADAS. Elas sao ARGUMENTO de uma tecnica, nao tecnica -- registra-las somava
		// seis no total de "tecnicas portadas" sem portar tecnica nenhuma, e esse total e a
		// unica medida honesta de quanto falta. O despacho as aceita mesmo sem registro,
		// porque este lote roda antes do gate generico.

	}

	// =====================================================================
	// DESPACHO
	// =====================================================================
	/// <summary>
	/// O DESPACHO DO LOTE. Devolve false quando o id nao e de nenhuma tecnica daqui.
	///
	/// A CHECAGEM DE "SABE A TECNICA" E FEITA AQUI DENTRO, e nao so no despacho geral, por causa
	/// das sub-escolhas: "Rock_Paper_Scissors_1" nao e um verb que skill nenhuma concede, entao o
	/// gate generico (que pergunta "alguma skill sua destrava este verb?") recusaria a escolha e
	/// deixaria passar so a execucao. Perguntando pelo id BASE, os dois passam ou os dois caem
	/// juntos, que e o unico jeito de a tecnica de duas fases fazer sentido.
	/// </summary>
	public bool UsarTecnicasG3(ServerPlayer pl, string id)
	{
		(string bas, int arg) = SepararG3(id);

		switch (bas)
		{
			case "Sword_Strike":
			case "Shield_Bash":
			case "Special_Multihit":
			case "Final_Explosion":
			case "Light_Buster":
			case "Bite":
			case "Rock_Paper_Scissors":
				break;
			default:
				return false;   // nao e minha
		}

		if (!SabeTecnica(pl, bas))
		{
			Avisar(pl, "você não sabe essa técnica.");
			return true;
		}

		switch (bas)
		{
			case "Sword_Strike": VarreduraG3(pl, arma: true); break;
			case "Shield_Bash": VarreduraG3(pl, arma: false); break;
			case "Special_Multihit": MultihitG3(pl); break;
			case "Final_Explosion": ExplosaoFinalG3(pl, arg); break;
			case "Light_Buster": LightBusterG3(pl); break;
			case "Bite": MorderG3(pl); break;
			case "Rock_Paper_Scissors": JokenpoG3(pl, arg); break;
		}
		return true;
	}

	/// <summary>
	/// Nome do lote com a assinatura que a regra geral pediu (`UsarTecnicaG3`), pra que ligar o
	/// lote no despacho funcione com qualquer um dos dois nomes. Sao o mesmo metodo.
	/// </summary>
	public bool UsarTecnicaG3(ServerPlayer pl, string id) => UsarTecnicasG3(pl, id);

	/// <summary>
	/// Parte o id em BASE + ARGUMENTO NUMERICO ("Final_Explosion_25" -> "Final_Explosion", 25).
	///
	/// So corta quando o sufixo e inteiro: "Rock_Paper_Scissors" nao vira "Rock_Paper" + lixo, e
	/// nenhum verb do DM termina em "_numero".
	/// </summary>
	private static (string Base, int Arg) SepararG3(string id)
	{
		int corte = id.LastIndexOf('_');
		if (corte <= 0 || corte == id.Length - 1) return (id, 0);
		return int.TryParse(id[(corte + 1)..], out int n) ? (id[..corte], n) : (id, 0);
	}

	// =====================================================================
	// SWORD STRIKE / SHIELD BASH -- Cultivation.dm:283 e :403
	// =====================================================================
	/// <summary>
	/// `kireq = Ephysoff*4 + 10*KiSwordDrain + 10*KiBladeDrain` (`Cultivation.dm:285` e `:405`).
	/// Os dois verbs usam a MESMA conta, letra por letra.
	/// </summary>
	private static double CustoDaVarreduraG3(Fighter f)
		=> f.Ephysoff * 4 + 10 * DrenoDaEspadaG3 + 10 * DrenoDaLaminaG3;

	/// <summary>
	/// O CORPO DAS DUAS: acerta TODO MUNDO na frente de uma vez, somando 5 de dano em cada golpe
	/// (`doAttack(M,5,...)`).
	///
	/// O DEFEITO DO ORIGINAL, CONSERTADO: la o `usr.Ki-=kireq` esta DENTRO do laco de alvos --
	///
	///     for(var/mob/M in view(1))
	///         if(doAttack(M,5,...))
	///             usr.Ki-=kireq        &lt;-- uma vez POR ALVO
	///
	/// ou seja, girar a espada no meio de tres pessoas cobrava tres vezes o preco anunciado, e
	/// podia deixar o Ki NEGATIVO (nada re-checava `Ki>=kireq` depois do primeiro desconto). E o
	/// avesso do que a tecnica promete: ela existe pra ser eficiente contra grupo e virava a mais
	/// cara justamente contra grupo. Aqui cobra UMA vez, depois do laco, e so se algum golpe
	/// encostou -- girar no vazio nao custa energia (isso o original tambem fazia, e esta certo).
	///
	/// O `view(1)` do BYOND e a caixa 3x3 em volta -- pega ate quem esta atras. Aqui virou o CONE
	/// de melee do port: um giro de espada que acerta quem esta as suas costas nao e melhor que o
	/// original, e o port inteiro (soco, dash, alvo) ja fala em cone.
	/// </summary>
	private void VarreduraG3(ServerPlayer pl, bool arma)
	{
		// `!usr.med && !usr.train && !usr.KO && usr.canfight`
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		// SO O SHIELD BASH CHECA A FERRAMENTA NO ORIGINAL (`usr.KiShieldOn` em Cultivation.dm:406);
		// o Sword Strike NAO checa `KiSwordOn` -- da pra dar o golpe de espada sem espada nenhuma.
		// E esquecimento, nao desenho: a tecnica pende de Ki_Knight e a mensagem fala em lamina.
		// Aqui as duas exigem a propria ferramenta.
		if (arma && !TemBuff(pl, "Ki_Sword"))
		{
			Avisar(pl, "você precisa da Ki Sword na mão pra dar esse golpe.");
			return;
		}
		if (!arma && !EscudoDePeG3(pl))
		{
			Avisar(pl, "sem o Ki Shield erguido não ha com o que golpear.");
			return;
		}

		long agora = NowMs();
		if (_prontoG3.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"seus golpes especiais ainda se recompoem (faltam {(livre - agora) / 1000.0:0.0}s).");
			return;
		}

		double custo = CustoDaVarreduraG3(pl.Ficha);
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return; }

		_prontoG3[pl.Id] = agora + BasicCdG3;

		string relato = arma ? "gira e abre um corte em" : "avanca com o escudo e acerta";
		int pegos = 0;
		bool encostou = false;

		// `.ToList()` porque um golpe pode matar alguem e mexer na lista da zona no meio do laco
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate == null || o.Combate.Intocavel) continue;
			if (!MeleeArea.NoAlcance(pl.Pos, pl.Facing, o.Pos)) continue;

			GolpeResultado r = GolpeG3(pl, o, addDano: 5, nivel: 2);
			pegos++;
			encostou |= r.Encostou;
			Avisar(o, $"{pl.Name} {relato} você.");
		}

		// A COBRANCA UNICA (o conserto). Fora do laco e condicionada a ter encostado em alguem.
		if (encostou) pl.Ficha.Ki -= custo;

		MandarEfeito(pl, arma ? "sword_strike" : "shield_bash", 400);
		Avisar(pl, pegos == 0
			? "você gira no vazio: ninguém estava na sua frente."
			: $"o golpe pega {pegos} {(pegos == 1 ? "pessoa" : "pessoas")}"
			  + (encostou ? $" (-{custo:0} de energia)." : ", mas nenhum acertou de verdade."));
	}

	/// <summary>
	/// O ESCUDO ESTA DE PE? Pergunta nos dois lugares onde ele pode viver.
	///
	/// O Ki Shield ja portado (<see cref="KiShield"/>) guarda o bonus em `_escudoAtivo`, porque e
	/// anterior ao alicerce de buffs; o alicerce chegou depois e um dia o escudo migra pra la. As
	/// duas perguntas custam um lookup cada e evitam que a migracao quebre o Shield Bash calado --
	/// que e o pior jeito de uma tecnica quebrar, porque parece falta de energia.
	/// </summary>
	private bool EscudoDePeG3(ServerPlayer pl)
		=> _escudoAtivo.ContainsKey(pl.Id) || TemBuff(pl, "Ki_Shield");

	// =====================================================================
	// SPECIAL MULTIHIT -- Martial Skill Attacks.dm:122
	// =====================================================================
	/// <summary>
	/// BARRAGEM NUM ALVO SO. `kireq = Ephysoff*BaseDrain*6` (`:124`), ate
	/// `min(10, Etechnique*2)` golpes (`:127`) de `+2` de dano, com `delay=2` (dois decimos)
	/// entre um e outro (`:128`).
	///
	/// O DEFEITO DO ORIGINAL, CONSERTADO: a tecnica era DE GRACA. O verb faz
	///
	///     if(BarrageAttack(2,FALSE,FALSE,"fires a punch at",amount,2))
	///         usr.Ki-=kireq
	///
	/// e o `BarrageAttack` (`attack_proc.dm:120-155`) nao tem `return` nenhum -- termina em
	/// `attacking=0` e devolve null. Null e falso, entao a linha do desconto NUNCA rodava: o
	/// jogador checava ter `kireq` de Ki e saia sem gastar um ponto. Aqui o custo e cobrado UMA
	/// vez, na hora de disparar a barragem, antes do primeiro golpe.
	///
	/// A CADENCIA DE 0,2s SAI DO PULSO DO LOTE, nao de `sleep`. O primeiro golpe sai na hora
	/// (senao a tecnica pareceria travada) e os outros entram na agenda.
	/// </summary>
	private void MultihitG3(ServerPlayer pl)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		long agora = NowMs();
		if (_prontoG3.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"seus golpes especiais ainda se recompoem (faltam {(livre - agora) / 1000.0:0.0}s).");
			return;
		}
		if (_barragemG3.ContainsKey(pl.Id)) { Avisar(pl, "você ja está no meio de uma barragem."); return; }

		double custo = pl.Ficha.Ephysoff * pl.Ficha.BaseDrain() * 6;
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return; }

		ServerPlayer? alvo = AlvoNaFrente(pl);
		if (alvo == null) { Avisar(pl, "não ha ninguém colado em você pra barrar de socos."); return; }

		// `min(10, Etechnique*2)`. No DM o `while(attackNum)` decrementa de 1 em 1 a partir de um
		// numero que pode ser fracionario (2,5 -> 1,5 -> 0,5 -> -0,5, e -0,5 ainda e "verdadeiro"
		// no DM); quem segurava o laco era o `cur_atk_num > first_atk_num`, o que na pratica dava
		// o teto inteiro. Aqui e um inteiro de verdade, com piso 1 -- um personagem de Tecnica
		// baixissima nao merece uma barragem de ZERO socos que ainda cobra o Ki.
		int golpes = (int)Math.Clamp(Math.Floor(pl.Ficha.Etechnique * 2), 1, 10);

		pl.Ficha.Ki -= custo;                       // o conserto: cobra, e cobra uma vez so
		_prontoG3[pl.Id] = agora + BasicCdG3;

		Avisar(pl, $"você afunila {golpes} socos em {alvo.Name} (-{custo:0} de energia).");
		MandarEfeito(pl, "barragem", golpes * BarragemPassoMs);

		GolpeG3(pl, alvo, addDano: 2, nivel: 2);    // o primeiro sai agora
		if (golpes <= 1) return;

		_barragemG3[pl.Id] = new BarragemG3
		{
			Alvo = alvo.Id,
			Faltam = golpes - 1,
			AddDano = 2,
			ProximoMs = agora + BarragemPassoMs,
		};
		LigarRelogioG3();
	}

	// =====================================================================
	// FINAL EXPLOSION -- misc.dm:296
	// =====================================================================
	/// <summary>
	/// AS DUAS FASES. Primeiro aperto escolhe o raio e prende o corpo carregando; segundo aperto
	/// detona.
	///
	/// O original abre uma caixa de dialogo (`input(...) in list("Maximum","Midrange","Closerange",
	/// "Cancel")`, `misc.dm:341`). O canal de habilidade do port manda um id e mais nada, entao a
	/// escolha virou sufixo: `Final_Explosion_3`, `_10` e `_25` (os mesmos tres numeros de
	/// `misc.dm:345-350`). Apertar `Final_Explosion` sem carga nenhuma LISTA as opcoes em vez de
	/// escolher por conta propria -- escolher sozinho seria escolher errado na metade das vezes,
	/// e o raio maximo mata o proprio jogador.
	///
	/// QUEM USA MORRE? SIM, e isso e o pilar da tecnica: `if(chargecounter>10) if(prob(70)||HP&lt;=10
	/// ||KO) spawn usr.Death()` (`misc.dm:319-323`). Carregar alem de vinte e cinco segundos e uma
	/// aposta de 70% na propria vida. Sem essa parte a tecnica seria so uma bomba gratis com tempo
	/// de espera, e nao a jogada desesperada que ela e no anime.
	/// </summary>
	private void ExplosaoFinalG3(ServerPlayer pl, int raio)
	{
		if (_cargaG3.TryGetValue(pl.Id, out CargaG3? carga)) { DetonarG3(pl, carga); return; }

		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "você não está em condições."); return; }

		if (raio != RaioPertoG3 && raio != RaioMedioG3 && raio != RaioMaximoG3)
		{
			Avisar(pl, "escolha o tamanho do estouro: 3 tiles (curto), 10 (medio) ou 25 (maximo).");
			Avisar(pl, "ATENCAO: no maximo, e com poder alto, a explosão leva o planeta -- e leva você.");
			return;
		}

		long agora = NowMs();
		_cargaG3[pl.Id] = new CargaG3
		{
			Raio = raio,
			Contador = 1,                       // `chargecounter=1` (misc.dm:340)
			Ancora = pl.Pos,                    // o `move=0` (misc.dm:355) virou ancora
			ProximoMs = agora + CargaPassoMs,
			ComecouMs = agora,
		};
		LigarRelogioG3();

		Avisar(pl, "você comeca a juntar TODA a energia em volta dentro do corpo. Você não consegue mais se mexer.");
		Avisar(pl, "aperte Final Explosion de novo pra detonar. Quanto mais carregar, mais forte -- e mais perigoso pra você.");
		MandarEfeito(pl, "carga_final", -1);
		GD.Print($"[server] {pl.Name} comecou a carregar Final Explosion (raio {raio})");
	}

	/// <summary>
	/// O ESTOURO. Todas as contas sao as de `misc.dm:309-318`:
	///
	///   power = (expressedBP * Ekioff) / (100 / chargecounter)
	///   nos outros: DamageCalc(power / dist, M.expressedBP * M.Ekidef, 10)
	///   em si:      DamageCalc(power, expressedBP * Ekidef, 10)
	///   Ki = max(Ki - raio * (MaxKi / 25), 0)
	///
	/// `DamageCalc(up, down, base)` e `(up/down)*base` (`calcs.dm:1-7`), com `down` virando 1
	/// quando chega zero.
	///
	/// DIVISAO POR DISTANCIA ZERO: o original faz `power/get_dist(src,M)` e `get_dist` devolve 0
	/// pra quem esta no MESMO tile -- divisao por zero, runtime, e o alvo colado sai ileso. Aqui a
	/// distancia tem piso de um tile, entao quem esta abracado com a bomba leva o valor cheio, que
	/// e obviamente o que se queria.
	///
	/// O DANO ENTRA COMO LETAL. No original o `SpreadDamage` de uma chamada so cai no ramo
	/// nao-letal (no DM `null == 0` e verdadeiro) e a morte vem de um `if(M.HP&lt;=10)` logo
	/// depois -- o piso do nao-letal la e 10% da vida do membro, exatamente o numero testado. No
	/// port o piso do nao-letal e 20%, entao esse `HP<=10` seria INALCANCAVEL e a explosao nunca
	/// mataria ninguem. Como a intencao do original e clara (ele imprime "[M] was killed by
	/// [usr]!" e pode destruir o planeta), o dano entra letal e a regra normal de corpo decide.
	/// </summary>
	private void DetonarG3(ServerPlayer pl, CargaG3 carga)
	{
		_cargaG3.Remove(pl.Id);
		MandarEfeito(pl, "carga_final", 0);

		Fighter f = pl.Ficha;
		double power = f.expressedBP * f.Ekioff * carga.Contador / 100.0;   // misc.dm:309

		Falar(pl, Protocol.Fala.Diz, "AAAAAAH!!");
		AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} explode!");

		int pegos = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate == null || o.Combate.Intocavel) continue;

			double distTiles = Math.Max(Vec2.Distance(o.Pos, pl.Pos) / ZoneCollision.TileSize, 1);
			if (distTiles > carga.Raio) continue;

			double baixo = Math.Max(o.Ficha.expressedBP * o.Ficha.Ekidef, 1);   // o `if(!downscalar) downscalar=1`
			double dano = power / distTiles / baixo * 10;                        // misc.dm:311
			EspalharDanoG3(o, pl, dano, letal: true);
			MandarEfeito(o, "explosao_final", 600);
			Avisar(o, $"a explosão de {pl.Name} passa por cima de você.");
			pegos++;
		}

		// O ESTOURO PEGA QUEM O SOLTOU, sempre -- `SpreadDamage(DamageCalc(power, expressedBP*Ekidef,
		// 10))` (misc.dm:317). Nao e efeito colateral: e o preco de ter juntado toda a energia
		// dentro do proprio corpo.
		double meuBaixo = Math.Max(f.expressedBP * f.Ekidef, 1);
		EspalharDanoG3(pl, pl, power / meuBaixo * 10, letal: true);

		// misc.dm:318 -- o custo escala com o RAIO escolhido, nao com o tempo de carga
		f.Ki = Math.Max(f.Ki - carga.Raio * (f.MaxKi / 25), 0);

		// misc.dm:319-323 -- a aposta. Carga acima de 10 (mais de 25s), 70% de morrer; ja
		// machucado ou nocauteado, morte certa.
		// O `Intocavel` ENTRA NO SORTEIO porque o `EspalharDanoG3` logo acima ja recusou o estrago: sem
		// esta perna, o corpo protegido nao levaria dano nenhum e mesmo assim morreria pelo `f.HP <= 10`
		// -- que e a vida que ele JA TINHA. E o mesmo erro de ler estado velho depois de um dano que nao
		// aconteceu, e ele aparece em todo lugar onde a consequencia mora fora do laco (ver
		// `GameServer.Sol.Queimar` e `EspalharDanoG2`).
		if (carga.Contador > CargaLetalG3
			&& (_rng.NextDouble() * 100 < CargaMortePct || f.HP <= 10 || f.KO)
			&& !f.dead
			&& !pl.Combate.Intocavel
			&& pl.Combate.Morrer())
		{
			AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} morre na propria Final Explosion.");
			GD.Print($"[server] {pl.Name} morreu na propria Final Explosion (carga {carga.Contador})");
		}

		Avisar(pl, $"a explosão sai com carga {carga.Contador} e pega {pegos} "
				   + $"{(pegos == 1 ? "pessoa" : "pessoas")} num raio de {carga.Raio} tiles.");
		GD.Print($"[server] Final Explosion de {pl.Name}: carga {carga.Contador}, raio {carga.Raio}, {pegos} atingidos");

		// ============================ E A EXPLOSAO PODE LEVAR O PLANETA ============================
		// `misc.dm:324-335`: `if(sdingrange >= 25 && expressedBP >= 10.000.000)` -> `isBeingDestroyed
		// = 1` + `DestroyPlanet`. Este era o gancho que dizia *"o port nao tem planeta destrutivel...
		// quando o sistema ganhar isso, o gancho e exatamente aqui"*. Ganhou.
		//
		// OS DOIS NUMEROS SAO DO ORIGINAL e nao inventados: o raio TEM que ser o maximo (25 tiles) e
		// o BP expresso tem que passar de dez milhoes. E a unica porta pra destruicao de planeta que
		// **nao pede ser vilao** -- e o preco dela e o de cima: quem carrega alem de 25 s tem 70% de
		// morrer junto. O jogador que apaga um mundo com isto costuma apagar a si mesmo.
		//
		// O ALGOZ E ELE MESMO (`pl.Id`), e o `mexpressedBP` e o BP dele -- ou seja, quem for mais
		// forte que o suicida sobrevive ao commit, exatamente como no Planet Destroy.
		// ====================================================================================
		if (carga.Raio >= RaioMaximoG3 && f.expressedBP >= BpQueLevaOPlanetaG3)
			ComecarDestruicao(pl.Zone, f.expressedBP, $"Final Explosion de {pl.Name}", pl.Id);
	}

	/// <summary>
	/// `if(usr.expressedBP >= 10000000)` (`misc.dm:324`) -- o BP expresso a partir do qual uma Final
	/// Explosion de raio maximo nao para no chao: ela leva o planeta.
	/// </summary>
	private const double BpQueLevaOPlanetaG3 = 10_000_000;

	// =====================================================================
	// LIGHT BUSTER -- yardrat.dm:233
	// =====================================================================
	/// <summary>
	/// GRITA, SOME, APARECE ATRAS E BATE QUATRO VEZES. `sayType("Light Buster!",3)` +
	/// `sleep(5)` + `flick('Zanzoken.dmi')` + quatro `doAttack(target,2,...)`.
	///
	/// PARA ONDE O TELEPORTE LEVA -- e onde o original se contradiz. A descricao da skill diz
	/// "deal several attacks, then move behind them" (`yardrat.dm:202`), mas o codigo faz
	///
	///     var/d = get_dir(target,src)      // direcao do ALVO ate MIM
	///     Move(get_step(target,d),d)       // o tile vizinho do alvo NO MEU LADO
	///
	/// ou seja, ele so encosta em voce pelo lado de onde voce ja veio -- de "atras" nao tem nada.
	/// Aqui vale a descricao: o destino e o tile atras do alvo em relacao a CARA dele, que e o que
	/// o nome e o texto prometem e o que da o bonus de flanco no funil de dano (1,5x pelas costas).
	/// Se aquele ponto estiver dentro de parede, cai no destino literal do DM (o meu lado); se
	/// esse tambem estiver, o golpe sai de onde eu estou.
	///
	/// A COLISAO E SO NO DESTINO, nao no caminho -- ao contrario do dash. E teleporte: o proprio
	/// texto diz "to everyone else, it'll look like you just teleported". Barrar pelo caminho
	/// transformaria a tecnica num dash com nome bonito.
	///
	/// NAO TEM CUSTO DE KI NEM RECARGA NO ORIGINAL, e nao inventei nenhum dos dois. O unico freio
	/// que existe la e o meio segundo de grito antes do golpe, e ele esta portado. O que ha aqui e
	/// uma trava de reentrada: nao da pra empilhar dois Light Busters, porque a agenda deste lote
	/// guarda um teleporte por pessoa.
	/// </summary>
	private void LightBusterG3(ServerPlayer pl)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }
		if (_busterG3.ContainsKey(pl.Id)) { Avisar(pl, "você ja está no meio de um Light Buster."); return; }

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, AlcanceBusterG3);
		if (alvo == null) { Avisar(pl, "precisa de um alvo a ate 4 tiles."); return; }

		Falar(pl, Protocol.Fala.Diz, "Light Buster!");
		MandarEfeito(pl, "zanzoken", BusterAvisoMs);

		_busterG3[pl.Id] = new BusterG3 { Alvo = alvo.Id, QuandoMs = NowMs() + BusterAvisoMs };
		LigarRelogioG3();
	}

	/// <summary>O teleporte propriamente dito, meio segundo depois do grito.</summary>
	private void ResolverBusterG3(ServerPlayer pl, BusterG3 b)
	{
		if (!_players.TryGetValue(b.Alvo, out ServerPlayer? alvo)
			|| alvo.Ficha.dead || alvo.Combate == null || alvo.Combate.Intocavel
			|| alvo.Zone.Hash != pl.Zone.Hash
			|| Vec2.Distance(alvo.Pos, pl.Pos) > AlcanceBusterG3)
		{
			Avisar(pl, "o alvo sumiu antes de você chegar.");
			return;
		}
		if (!ProntoPraGolpeG3(pl, out _)) return;   // caiu no meio do meio segundo

		ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;

		// 1) o que a skill PROMETE: atras do alvo, em relacao a cara dele
		Vec2 atras = alvo.Pos - MeleeArea.Frente(alvo.Facing) * DistanciaDeParada;
		// 2) o que o codigo do DM faz: o vizinho do alvo do MEU lado
		Vec2 meuLado = alvo.Pos + (pl.Pos - alvo.Pos).Normalized() * DistanciaDeParada;

		Vec2? destino = null;
		if (mapa == null || !MoveRules.Occupied(mapa, atras)) destino = atras;
		else if (!MoveRules.Occupied(mapa, meuLado)) destino = meuLado;

		if (destino is { } d)
		{
			Vec2 saiuDe = pl.Pos;   // pra miragem: e daqui que o vulto nasce
			pl.Pos = d;
			pl.Facing = MoveRules.FacingFrom(alvo.Pos - d, pl.Facing);
			pl.LastInputMs = NowMs();
			pl.CorrecaoEsperadaAte = NowMs() + 500;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
			pl.SeqDoTeleporte = pl.SeqInput;   // os pacotes em voo do cliente sao da posicao velha
			MandarCorrecaoG3(pl);

			// A ZONA PRECISA VER. Este era o TERCEIRO caminho de deslocamento do servidor (com o dash
			// e o Zanzoken) e o unico que so avisava o proprio jogador: pra quem estava olhando, o
			// corpo simplesmente aparecia do outro lado do alvo, sem nada explicando. O `AnunciarZanzo`
			// leva o ponto de PARTIDA e ja e o canal unico da piscada.
			AnunciarZanzo(pl, saiuDe);
		}

		// quatro `doAttack(target,2,...)` em sequencia (yardrat.dm:241-244)
		for (int i = 0; i < 4; i++)
		{
			if (alvo.Ficha.dead || alvo.Combate.Intocavel) break;
			GolpeG3(pl, alvo, addDano: 2, nivel: i == 3 ? 3 : 2);
		}

		// ============================ O EXP DA PROPRIA SKILL ============================
		// `yardrat.dm:245`: `Skill_EXP_Add(/datum/skill/yardrat/Light_Buster, 1)`, LOGO DEPOIS dos
		// quatro golpes e ainda dentro do `if(target in view(4))` -- ou seja, so paga quem de fato
		// alcancou o alvo. Este ponto e exatamente esse: quem nao alcancou saiu la em cima, no
		// desvio do "o alvo sumiu antes de voce chegar".
		//
		// SEM CONDICAO NENHUMA EM VOLTA, e isso e 1:1: o original credita mesmo se o alvo morreu no
		// meio da emenda (o `Skill_EXP_Add` esta fora do laco de golpes, nao dentro).
		//
		// E A UNICA CHAMADA DE `Skill_EXP_Add` EM TODO O DM. Os 30 contadores das arvores de Ki NAO
		// passam por aqui -- eles sobem por marca-d'agua dentro do proprio `effector()`
		// (`Effusion.dm:74-77`), que neste port e o `NiveisDeSkill.Efetor`. Ver `CreditarPorContador`.
		//
		// A regra existe no disco: `niveis.json:689` (barreira 100, maxnivel 2), e o degrau de nivel 2
		// e que traz o "Your skill at blitzing people got better...".
		// O `Creditar` recusa quem nao aprendeu a skill, entao nao ha progresso fantasma a conferir aqui.
		// ==============================================================================
		pl.Niveis.Creditar("/datum/skill/yardrat/Light_Buster", 1);

		Avisar(alvo, $"{pl.Name} some e reaparece em cima de você.");
		Avisar(pl, $"você aparece nas costas de {alvo.Name} e emenda quatro golpes.");
		MandarEfeito(alvo, "zanzoken", 300);
	}

	// =====================================================================
	// BITE -- Vampires.dm:80 (o verb) e :15 (o VampBite)
	// =====================================================================
	/// <summary>
	/// MORDE QUEM ESTA COLADO. Alimenta, machuca, e as vezes rende poder que fica.
	///
	/// O DEFEITO DO ORIGINAL, CONSERTADO -- A MORDIDA NAO DAVA DANO NENHUM. `VampBite`
	/// (`Vampires.dm:24-31`) faz:
	///
	///     var/phystechcalc                                   // sem valor: null
	///     var/opponentphystechcalc                           // sem valor: null
	///     if(Ephysoff&lt;1||Etechnique&lt;1)  phystechcalc = ...   // SO nesse caso
	///     if(TargetMob.Ephysoff&lt;1||...)  opponentphystechcalc = ...
	///     var/dmg = DamageCalc(phystechcalc, opponentphystechcalc, Ephysoff*3)
	///
	/// Quem tem Ephysoff e Etechnique acima de 1 -- ou seja, TODO personagem que treinou um dia --
	/// chega no `DamageCalc` com `up = null`, e `(0/1)*base` e zero. A mordida era cosmetica pra
	/// todo mundo menos pra um recem-nascido. Aqui o golpe passa pelo funil normal de melee, que
	/// e o que o pedido do lote pede e o que faz a mordida respeitar guarda, membro e Zenkai como
	/// qualquer outro golpe; o `Ephysoff*3` do original entra como dano somado, que e o papel que
	/// ele tinha la (o `basedamage` do DamageCalc). O `*2` e o `BPModulus` da linha 31 nao sao
	/// repetidos porque o funil ja aplica o proprio gap de poder -- repetir seria elevar ao
	/// quadrado o mesmo fator.
	///
	/// A NUTRICAO: o original soma `currentNutrition += 40`. O port nao tem esse campo; o canal de
	/// alimentacao aqui e o `staminadeBuff` (0-100, o mesmo que o CheckNutrition do DM escrevia),
	/// entao os 40 vao pra la, com teto em 100.
	///
	/// A RECARGA DE 300s NAO IMPEDE MORDER -- ela so fecha a porta do BONUS. E assim no original
	/// (`if(!vampcooldown && prob(5))`, `Vampires.dm:17`): morder e livre, ENRIQUECER com a
	/// mordida e que tem espera de cinco minutos.
	/// </summary>
	private void MorderG3(ServerPlayer pl)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		// `doAttack` do DM ja respeitava a cadencia do proprio corpo (`attacking = Eactspeed/3`);
		// aqui isso e o `PodeAtacar` do estado de combate. Sem ele a mordida seria um soco extra
		// sem recarga e sem custo -- ou seja, melhor que socar, sempre.
		if (!pl.Combate.PodeAtacar()) { Avisar(pl, "seu corpo ainda não se recompos do último golpe."); return; }

		ServerPlayer? alvo = AlvoNaFrente(pl);
		if (alvo == null) { Avisar(pl, "não ha ninguém colado em você."); return; }
		if (alvo.Ficha.dead) { Avisar(pl, "não da pra morder um morto: não ha nutriente nenhum ali."); return; }

		long agora = NowMs();
		pl.Combate.Recarga = CombatMath.Cadencia(pl.Ficha);
		pl.AtaqueAte = agora + (long)(pl.Combate.Recarga * 1000);

		GolpeResultado r = GolpeG3(pl, alvo, addDano: pl.Ficha.Ephysoff * 3, nivel: 2);
		MandarEfeito(alvo, "mordida", 300);

		// `currentNutrition += 40` -- e vem mesmo que o golpe tenha sido aparado: a boca chegou.
		pl.Ficha.staminadeBuff = Math.Min(pl.Ficha.staminadeBuff + 40, 100);

		// `if(!vampcooldown && prob(5))` (Vampires.dm:17-22)
		bool naEspera = _mordidaProntaG3.TryGetValue(pl.Id, out long livre) && agora < livre;
		if (!naEspera && _rng.NextDouble() * 100 < VampProcPct)
		{
			_mordidaProntaG3[pl.Id] = agora + MordidaRecargaMs;
			pl.Ficha.ParanormalBPMult += 0.1;
			pl.Ficha.stamina = Math.Min(pl.Ficha.stamina + 20, pl.Ficha.maxstamina);
			pl.Ficha.ParanormalBPMult = Math.Max(Math.Min(VampMultMax, pl.Ficha.ParanormalBPMult), VampMultMin);
			pl.Ficha.Statify();
			pl.SigAtributos = "";

			Avisar(pl, $"o sangue desce quente e ALGUMA COISA fica: você sai destá mais forte "
					   + $"(x{pl.Ficha.ParanormalBPMult:0.0} permanente).");
			GD.Print($"[server] {pl.Name} ganhou poder mordendo {alvo.Name}"
					 + $" (ParanormalBPMult {pl.Ficha.ParanormalBPMult:0.00})");
		}
		else
		{
			Avisar(pl, naEspera
				? "você se alimenta, mas o corpo ainda está digerindo o último banquete."
				: "você se alimenta.");
		}

		Avisar(alvo, r.Encostou
			? $"{pl.Name} crava os dentes em você."
			: $"{pl.Name} tenta te morder e não alcanca.");

		// FICOU DE FORA: "transformar" a vitima (`turnChoice == "Yes"` + `prob(30)` ->
		// `Vampirification`, Vampires.dm:32-34). O port nao tem vampirismo nem licantropia como
		// estado -- nao ha `IsAVampire`, nao ha mastery escondida, nao ha o que ligar. A mordida
		// aqui alimenta e fortalece; nao converte.
	}

	// =====================================================================
	// ROCK PAPER SCISSORS -- saibaman.dm:84
	// =====================================================================
	/// <summary>
	/// O JOKENPO DO SAIBAMAN: uma leitura em duas etapas.
	///
	/// PRIMEIRO APERTO escolhe em segredo (`S.rps = n`, `saibaman.dm:111-115`). SEGUNDO APERTO
	/// avanca dois passos e soca, com `+10` de dano se a escolha tiver adivinhado o que o alvo
	/// estava fazendo (`saibaman.dm:88-106`):
	///
	///   1 PEDRA   pune quem NAO esta atacando
	///   2 PAPEL   pune quem ESTA atacando
	///   3 TESOURA pune quem esta BLOQUEANDO
	///
	/// E O QUE FAZ A TECNICA INTERESSANTE: voce aposta antes de saber. Guardar a escolha em vez de
	/// resolver na hora e o desenho inteiro -- se desse pra escolher depois de ver o inimigo,
	/// seria so um soco de +10 sempre.
	///
	/// O CANAL MANDA UM ID SO, entao a escolha vem como sufixo: `Rock_Paper_Scissors_1/2/3`. O id
	/// pelado executa.
	///
	/// "ESTA ATACANDO" no port e a janela de pose de ataque (`AtaqueAte`), que e exatamente o que
	/// o `attacking` do DM era: um contador que fica de pe enquanto o golpe acontece.
	///
	/// UM DETALHE DO ORIGINAL QUE FOI MANTIDO: a escolha e consumida mesmo quando o alvo esta longe
	/// demais (`S.rps = 0` esta fora dos `if(target in view(2))`, `saibaman.dm:108`). Errar o
	/// tempo custa a aposta -- e por isso que o jokenpo nao e so dano de graca.
	/// </summary>
	private void JokenpoG3(ServerPlayer pl, int escolha)
	{
		if (escolha is >= 1 and <= 3)
		{
			_jokenpoG3[pl.Id] = escolha;
			string nome = escolha switch { 1 => "PEDRA", 2 => "PAPEL", _ => "TESOURA" };
			Avisar(pl, $"você guarda {nome} na manga. No próximo aperto você avanca e usa.");
			return;
		}

		if (!_jokenpoG3.TryGetValue(pl.Id, out int guardada) || guardada == 0)
		{
			// o original abre um `input()` pedindo 1, 2 ou 3; aqui a lista vai pro chat
			Avisar(pl, "escolha primeiro: 1 pedra (pune quem não ataca), 2 papel (pune quem ataca), "
					   + "3 tesoura (pune quem bloqueia).");
			return;
		}

		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		// A ESCOLHA SAI DA MANGA AGORA, aconteca o que acontecer daqui pra frente.
		_jokenpoG3.Remove(pl.Id);

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, AlcanceJokenpoG3);
		if (alvo == null) { Avisar(pl, "ninguém a dois tiles: sua jogada se perde."); return; }

		// `step_towards(src,target)` duas vezes -- dois tiles de avanco, parando encostado
		AvancarG3(pl, alvo, 2 * ZoneCollision.TileSize);

		bool atacando = NowMs() < alvo.AtaqueAte;
		bool bloqueando = alvo.Combate?.Bloqueando == true;
		bool acertou = guardada switch
		{
			1 => !atacando,     // pedra
			2 => atacando,      // papel
			_ => bloqueando,    // tesoura
		};

		GolpeG3(pl, alvo, addDano: acertou ? 10 : 0, nivel: acertou ? 3 : 1);
		MandarEfeito(pl, "jokenpo", 300);

		string mao = guardada switch { 1 => "pedra", 2 => "papel", _ => "tesoura" };
		Avisar(pl, acertou
			? $"você leu {alvo.Name} certinho: {mao} pega em cheio."
			: $"você mostra {mao} e não era a hora -- o golpe sai fraco.");
		Avisar(alvo, $"{pl.Name} avanca e ataca com {mao}.");
	}

	// =====================================================================
	// O PULSO DO LOTE
	// =====================================================================
	/// <summary>Cadencia do relogio proprio deste lote, em segundos.</summary>
	private const double PassoG3 = 0.1;

	private bool _relogioG3;

	/// <summary>
	/// LIGA O RELOGIO se houver o que fazer. Ele se desliga sozinho quando a agenda esvazia --
	/// um servidor com ninguem carregando nada nao paga nada por este lote.
	/// </summary>
	private void LigarRelogioG3()
	{
		if (_relogioG3 || !IsInsideTree()) return;
		_relogioG3 = true;
		AgendarPulsoG3();
	}

	private void AgendarPulsoG3()
	{
		SceneTree? arv = IsInsideTree() ? GetTree() : null;
		if (arv == null) { _relogioG3 = false; return; }

		SceneTreeTimer t = arv.CreateTimer(PassoG3);
		t.Timeout += () =>
		{
			try { PulsoG3(); }
			catch (Exception ex) { GD.PushWarning($"[G3] pulso: {ex.Message}"); }

			if (_barragemG3.Count == 0 && _cargaG3.Count == 0 && _busterG3.Count == 0)
			{
				_relogioG3 = false;
				return;
			}
			AgendarPulsoG3();
		};
	}

	private void PulsoG3()
	{
		long agora = NowMs();
		PulsoBarragemG3(agora);
		PulsoCargaG3(agora);
		PulsoBusterG3(agora);
	}

	/// <summary>
	/// A BARRAGEM, golpe a golpe. As duas condicoes de parada sao do `BarrageAttack`
	/// (`attack_proc.dm:143` e `:151`): o alvo se AFASTOU dois tiles, ou ergueu a GUARDA.
	///
	/// A segunda e a que da graca a tecnica: uma barragem parada por bloqueio e uma barragem
	/// desperdicada, entao vale esperar o inimigo baixar a guarda em vez de despejar sempre.
	/// </summary>
	private void PulsoBarragemG3(long agora)
	{
		foreach (int id in _barragemG3.Keys.ToList())
		{
			BarragemG3 b = _barragemG3[id];
			if (!_players.TryGetValue(id, out ServerPlayer? pl)
				|| pl.Ficha.dead || pl.Ficha.KO || pl.Combate == null)
			{
				_barragemG3.Remove(id);
				continue;
			}
			if (agora < b.ProximoMs) continue;

			if (!_players.TryGetValue(b.Alvo, out ServerPlayer? alvo)
				|| alvo.Ficha.dead || alvo.Combate == null || alvo.Combate.Intocavel
				|| alvo.Zone.Hash != pl.Zone.Hash)
			{
				_barragemG3.Remove(id);
				continue;
			}
			if (Vec2.Distance(alvo.Pos, pl.Pos) >= BarragemDistanciaMaxG3)
			{
				_barragemG3.Remove(id);
				Avisar(pl, "seu alvo saiu do alcance e a barragem morre no ar.");
				continue;
			}

			// AVANCA ANTES DE BATER (Furacao). O `AvancarG3` respeita parede, entao a sequencia nao
			// empurra ninguem pra dentro do cenario -- ver o bloco de la.
			if (b.AvancaPx > 0) AvancarG3(pl, alvo, b.AvancaPx);

			// O KNOCKBACK DESLIGADO E DO ATACANTE, e ele volta LOGO DEPOIS do golpe: e literalmente o
			// `saveknock` do original. Guardar e devolver (em vez de recalcular) e a mesma regra dos
			// buffs -- quem liga desliga com o valor que achou.
			bool guardado = pl.Knockback;
			if (b.SemEmpurrao) pl.Knockback = false;
			// A ESCADA MANDA QUANDO EXISTE (combos de boxe do lote G7); sem ela o dano da sequencia e
			// o mesmo em todos os golpes, que e o Multihit e a Presa do Lobo.
			double addDano = b.Escada != null && b.EscadaIdx < b.Escada.Length
				? b.Escada[b.EscadaIdx].Dano
				: b.AddDano;
			GolpeG3(pl, alvo, addDano: addDano, nivel: 2);
			pl.Knockback = guardado;

			if (alvo.Combate.Bloqueando)
			{
				_barragemG3.Remove(id);
				Avisar(pl, $"{alvo.Name} fecha a guarda e a barragem para.");
				continue;
			}

			b.Faltam--;
			b.EscadaIdx++;
			// O ATRASO DO PROXIMO DEGRAU sai da propria escada (`sleep(1)`/`sleep(2)` do DM); sem
			// escada continua sendo o `delay=2` de sempre.
			b.ProximoMs = agora + (b.Escada != null && b.EscadaIdx < b.Escada.Length
				? b.Escada[b.EscadaIdx].AtrasoMs
				: BarragemPassoMs);
			if (b.Faltam > 0) continue;

			// O ARREMESSO DO FIM (Punho da Presa do Lobo): `kbdir = usr.dir`, `kbpow = expressedBP`,
			// `kbdur = 4`. Ele sai DEPOIS do ultimo golpe e independe do sorteio do `TentarEmpurrar`
			// -- e a promessa da tecnica, nao uma consequencia do dano.
			if (b.ArremessoNoFim > 0 && !alvo.Ficha.dead)
				Arremessar(alvo, MeleeArea.Frente(pl.Facing), pl.Ficha.expressedBP, b.ArremessoNoFim);

			_barragemG3.Remove(id);
		}
	}

	/// <summary>
	/// A CARGA DA FINAL EXPLOSION: prende o corpo, engorda o contador e narra os dois marcos.
	///
	/// A TRAVA E POR ANCORA e nao por flag, porque a flag do original (`move=0`) e lida no laco de
	/// movimento do BYOND e o laco de movimento daqui vive noutro arquivo. Reescrever a posicao e
	/// devolver `Correction` da o mesmo resultado visivel: o personagem nao sai do lugar.
	///
	/// TOMAR UM NOCAUTE CANCELA. No original o `sding` continua de pe e o jogador acorda ainda
	/// carregando -- o que na pratica e uma bomba armada que ninguem controla. Aqui a carga cai
	/// junto com o corpo, como todo o resto (buff, escudo, forma).
	/// </summary>
	private void PulsoCargaG3(long agora)
	{
		foreach (int id in _cargaG3.Keys.ToList())
		{
			CargaG3 c = _cargaG3[id];
			if (!_players.TryGetValue(id, out ServerPlayer? pl))
			{
				_cargaG3.Remove(id);
				continue;
			}
			if (pl.Ficha.dead || pl.Ficha.KO)
			{
				_cargaG3.Remove(id);
				MandarEfeito(pl, "carga_final", 0);
				Avisar(pl, "a energia que você juntava se dispersa.");
				continue;
			}

			// a ancora: qualquer passo que o cliente tenha dado e desfeito aqui
			if (Vec2.Distance(pl.Pos, c.Ancora) > 4)
			{
				pl.Pos = c.Ancora;
				pl.LastInputMs = agora;
				pl.CorrecaoEsperadaAte = agora + 400;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
				pl.SeqDoTeleporte = pl.SeqInput;   // nao e cliente desonesto: e a trava
				MandarCorrecaoG3(pl);
			}

			// misc.dm:356-361 -- as duas narracoes, aos 2s e aos 6s
			long decorrido = agora - c.ComecouMs;
			if (c.Narrou < 1 && decorrido >= 2000)
			{
				c.Narrou = 1;
				AvisarPertoG3(pl, 20 * ZoneCollision.TileSize,
					$"{pl.Name} comeca a juntar toda a energia em volta dentro do corpo!");
			}
			else if (c.Narrou < 2 && decorrido >= 6000)
			{
				c.Narrou = 2;
				AvisarPertoG3(pl, 20 * ZoneCollision.TileSize,
					$"o chao comeca a tremer com forca em volta de {pl.Name}!");
				if (c.Raio >= RaioMaximoG3 && pl.Ficha.expressedBP >= 10_000_000)
					AvisarPertoG3(pl, 20 * ZoneCollision.TileSize,
						$"a energia em volta de {pl.Name} poderia levar o planeta!!");
			}

			// misc.dm:363-367 -- `spawn(25) chargecounter += 1` em laco
			if (agora < c.ProximoMs) continue;
			c.Contador++;
			c.ProximoMs = agora + CargaPassoMs;
			MandarEfeito(pl, "carga_final", -1);

			if (c.Contador == CargaLetalG3 + 1)
				Avisar(pl, "a energia passou do que seu corpo aguenta: detonar agora provavelmente te MATA.");
		}
	}

	private void PulsoBusterG3(long agora)
	{
		foreach (int id in _busterG3.Keys.ToList())
		{
			BusterG3 b = _busterG3[id];
			if (agora < b.QuandoMs) continue;
			_busterG3.Remove(id);

			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) continue;
			ResolverBusterG3(pl, b);
		}
	}

	// =====================================================================
	// FERRAMENTAS DO LOTE
	// =====================================================================
	/// <summary>
	/// A GUARDA DE ENTRADA DE TODA TECNICA DAQUI: o
	/// `!usr.med && !usr.train && !usr.KO && usr.canfight` que abre os quatro verbs do DM.
	///
	/// A RECUSA VEM COM MOTIVO porque motivo ENSINA. "Voce nao pode agora" nao explica que a
	/// tecnica nao sai meditando -- e o jogador desiste da tecnica em vez de sair da meditacao.
	/// </summary>
	private static bool ProntoPraGolpeG3(ServerPlayer pl, out string porque)
	{
		porque = "";
		if (pl.Combate == null) { porque = "seu corpo ainda nao esta pronto."; return false; }
		if (pl.Ficha.dead) { porque = "voce esta morto."; return false; }
		if (pl.Ficha.KO) { porque = "voce esta caido."; return false; }
		if (pl.Ficha.med) { porque = "nao da pra fazer isso meditando -- saia da meditacao primeiro."; return false; }
		if (pl.Ficha.train) { porque = "voce esta treinando; pare o treino pra usar tecnica."; return false; }
		if (pl.Combate.Stun > 0) { porque = "voce ainda esta atordoado."; return false; }
		return true;
	}

	/// <summary>
	/// UM GOLPE DE TECNICA PELO FUNIL NORMAL -- o equivalente do `doAttack(M, addeddamage, ...)`.
	///
	/// Faz a MESMA coisa que <see cref="Atacar"/> faz depois de escolher o alvo: resolve, paga o
	/// treino dos dois lados, trata o contra-ataque, aplica as consequencias e conta pra zona. O
	/// que NAO faz e mexer em recarga -- cada tecnica tem a sua (o `basicCD`), e uma barragem de
	/// dez golpes nao pode pagar dez recargas de soco.
	/// </summary>
	private GolpeResultado GolpeG3(ServerPlayer a, ServerPlayer d, double addDano, int nivel = 2)
	{
		CombatState ca = a.Combate, cd = d.Combate;
		double angulo = MeleeArea.AnguloDeChegada(d.Pos, d.Facing, a.Pos);

		// o dano de estilo entra somado, igual ao soco comum (`dmg += compareStyles(M)`)
		GolpeResultado r = MeleeResolver.Resolver(ca, cd, angulo, _rng, 1, addDano + DanoDeEstilo(a, d));
		MarcarAgressao(d, a);

		// O bit do mestre, igual ao soco comum -- ver o bloco em `GameServer.Combat.cs`.
		a.Ficha.AttackGain(_rng, a.Ficha.FightGainMult(d.Ficha, EhMeuMestre(a, d)));
		if (r.Encostou) d.Ficha.AttackGain(_rng, d.Ficha.FightGainMult(a.Ficha, EhMeuMestre(d, a)));

		// o contra-ataque de guarda perfeita vale contra tecnica tambem: quem bloqueou na hora
		// certa devolve, venha o golpe de um soco ou de uma espada de Ki
		if (r.Desfecho == Desfecho.Contra)
		{
			GolpeResultado devolta = MeleeResolver.Resolver(
				cd, ca, MeleeArea.AnguloDeChegada(a.Pos, a.Facing, d.Pos), _rng);
			MarcarAgressao(a, d);
			ResolverDesfecho(d, a, devolta);
			AnunciarGolpe(d, a, devolta, 2);
		}

		ResolverDesfecho(a, d, r);
		AnunciarGolpe(a, d, r, nivel);
		return r;
	}

	/// <summary>
	/// DANO ESPALHADO -- o `SpreadDamage` do original (`Injuries.dm:63-87`).
	///
	/// A diferenca pro soco e o que define uma explosao: o soco SORTEIA um membro, isto bate o
	/// MESMO valor em cada membro atingivel. Nao ha como uma bomba "errar o braco".
	///
	/// Os aninhados (cerebro, orgaos, maos, pes) nao entram direto -- eles pegam a fracao de
	/// propagacao pelo membro que os contem, que e como o <see cref="Body.Ferir"/> ja funciona. No
	/// original eles entravam com `prob(10)`, o que dava na mesma com mais sorteio.
	/// </summary>
	private void EspalharDanoG3(ServerPlayer vitima, ServerPlayer autor, double dano, bool letal)
	{
		CombatState? c = vitima.Combate;
		if (c == null || dano <= 0 || vitima.Ficha.dead || c.Intocavel) return;

		c.EntrarEmCombate();
		if (autor != vitima) autor.Combate?.EntrarEmCombate();

		foreach (BodyPart p in c.Corpo.Partes.ToList())
		{
			if (p.Decepado || p.Aninhado) continue;
			c.Ferir(p, dano, letal);   // pelo funil -- o `c.Intocavel` la em cima ja recusou a chamada
		}
		c.SincronizarVida();
		if (autor != vitima) MarcarAgressao(vitima, autor);

		// PELO FUNIL DA DERROTA, e nao pelo Zenkai direto: uma explosao que mata na frente dos
		// amigos da vitima e exatamente a cena que o `Death.dm` descreve. Ver `AoPerderALuta`.
		//
		// `autor != vitima` porque uma tecnica pode ferir quem a soltou (a Final Explosion pega
		// quem a usou, `misc.dm:317`) -- e morrer da propria explosao nao e "ser abatido por um
		// inimigo": nao ha algoz, e o DM tambem so enfurece com `deathKiller` preenchido.
		if (c.Corpo.DeveMorrer() && !c.Corpo.RegeneraDecepado && c.Morrer())
		{
			if (autor != vitima) AoPerderALuta(vitima, autor, morreu: true);
			else ZenkaiPorDerrota(vitima, autor);
			GD.Print($"[server] {autor.Name} MATOU {vitima.Name} com dano em area");
		}
		else if (!vitima.Ficha.KO && c.Corpo.DeveNocautear())
		{
			c.Nocautear(MeleeResolver.SegundosDeNocaute);
			if (autor != vitima) AoPerderALuta(vitima, autor, morreu: false);
			else ZenkaiPorDerrota(vitima, autor);
		}
	}

	/// <summary>
	/// O ALVO DE UMA TECNICA DE ALCANCE: o MARCADO primeiro (e so a distancia conta, como no
	/// resto do combate), senao o mais proximo dentro do raio.
	///
	/// O CONE NAO ENTRA aqui de proposito: `Light_Buster` e o jokenpo sao tecnicas de APROXIMACAO
	/// -- elas existem justamente pra alcancar quem nao esta colado na sua cara, e exigir que o
	/// alvo ja esteja no cone do soco tiraria a razao de existirem.
	/// </summary>
	private ServerPlayer? AlvoDeTecnicaG3(ServerPlayer a, float raioPx)
	{
		if (Marcado(a) is { } mira && Vec2.Distance(mira.Pos, a.Pos) <= raioPx) return mira;

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate == null || o.Combate.Intocavel) continue;
			float d2 = (o.Pos - a.Pos).LengthSquared;
			if (d2 > raioPx * raioPx || d2 >= melhorDist) continue;
			melhorDist = d2;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// AVANCA ate <paramref name="maxPx"/> na direcao do alvo, parando a um tile dele -- o
	/// `step_towards` repetido do original, adaptado pra posicao livre.
	///
	/// A PAREDE MANDA MAIS, igual ao dash (<see cref="Aproximar"/>): sem esta checagem, avancar
	/// contra um muro com alguem do outro lado seria o unico jeito de andar dentro da parede.
	/// </summary>
	private void AvancarG3(ServerPlayer a, ServerPlayer alvo, float maxPx)
	{
		Vec2 d = alvo.Pos - a.Pos;
		float dist = d.Length;
		if (dist <= DistanciaDeParada) return;

		float andar = Math.Min(maxPx, dist - DistanciaDeParada);
		Vec2 destino = a.Pos + d.Normalized() * andar;

		ZoneCollision? mapa = _catalogo?.Get(a.Zone)?.Mapa;
		if (mapa != null && MoveRules.PathOccupied(mapa, a.Pos, destino)) return;

		a.Pos = destino;
		a.Facing = MoveRules.FacingFrom(d, a.Facing);
		a.LastInputMs = NowMs();
		a.CorrecaoEsperadaAte = NowMs() + 500;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
		a.SeqDoTeleporte = a.SeqInput;
		MandarCorrecaoG3(a);
	}

	/// <summary>A posicao nova precisa chegar ao dono AGORA -- ver o comentario no dash.</summary>
	private static void MandarCorrecaoG3(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(pl.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
		// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
		// o cliente leria a coordenada X como se fosse a sequencia.
		w.PutVec(pl.Pos);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>O `to_chat(range(N), ...)` do original: uma linha pra todo mundo por perto.</summary>
	private void AvisarPertoG3(ServerPlayer centro, float raioPx, string texto)
	{
		float raio2 = raioPx * raioPx;
		foreach (ServerPlayer o in ZoneList(centro.Zone.Hash))
			if ((o.Pos - centro.Pos).LengthSquared <= raio2) Avisar(o, texto);
	}
}
