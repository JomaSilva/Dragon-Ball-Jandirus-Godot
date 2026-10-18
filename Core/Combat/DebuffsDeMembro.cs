namespace Jandirus.Core.Combat;

/// <summary>
/// ============================ O QUE UM MEMBRO PERDIDO (E UM CORPO FERIDO) CUSTA ============================
/// O dono, depois do playtest de 2026-10-10: *"ser desmembrado n tava dando debuffs reais como eram o DM"*.
/// Ele tinha razao, e a razao e de DATA: o DM ganhou estas regras num commit so (`601a30b`, 2026-06-29,
/// "limb loss effects" -- os campos `armsLost`/`legsLost` de `mobparts_logic.dm:28-29`, recontados a cada
/// `HealthSync`, `Injuries.dm:224-230` e `:274-275`). O combate do port e de antes: ele sabia ARRANCAR o
/// braco (a peca no chao, os 20% de Ki, o membro que some do sprite, o roxo do boneco) e nao sabia COBRAR
/// por ele. Quem perdia as duas pernas saia correndo.
///
/// UMA FUNCAO POR PERGUNTA, e cada uma e uma linha do original:
///
///   <see cref="FatorDePontaria"/>   braco perdido erra mais             `CombatMovement.dm:189`
///   <see cref="RitmoDaRecarga"/>    sem os dois bracos o soco demora    `movement handler.dm:167`
///   <see cref="Corre"/>             sem uma perna nao ha corrida        `speedy.dm:36-38` e `:54`, `movement handler.dm:50`
///   <see cref="AndaNoChao"/>        sem as duas so se anda voando       `movement handler.dm:133`
///   <see cref="FatorDoPasso"/>      corpo ferido anda devagar           `movement handler.dm:55`
///
/// ============================ NADA AQUI E GUARDADO, E E ISSO QUE FAZ O DEBUFF ACABAR ============================
/// O DM guarda (`armsLost`, `legsLost`) e reconta a cada tique. Aqui nem campo ha: toda resposta e funcao pura
/// do CORPO de agora (<see cref="De"/>). O membro que volta -- pelo tanque, pela regeneracao da raca, pelo
/// `regenerar` do Namekuseijin, pela morte que devolve o corpo inteiro, pelo Heal de admin, pela fusao -- tira o
/// debuff no mesmo instante, e SO ele tira: nao ha bit pra ninguem esquecer de apagar em oito lugares.
///
/// ============================ E A TELA LE DAQUI ============================
/// As frases da aba Body (<see cref="NotaDoMembro"/>, <see cref="EmVigor"/>) e a do chat na hora da perda
/// (<see cref="FraseDaPerda"/>) sao montadas com as MESMAS funcoes e as MESMAS constantes que o servidor
/// consulta pra aplicar. Um numero escrito a mao num rotulo e um numero que mente no dia em que alguem
/// ajustar a constante.
///
/// QUEM APLICA NAO MORA AQUI: `MeleeResolver.Resolver` (a pontaria), `CombatState.Tick` (a recarga, por gancho)
/// e `GameServer.Membros.cs` (a corrida, o chao e o passo -- com a pergunta "quem dirige este corpo?", que e
/// do servidor).
/// ==========================================================================================================
/// </summary>
public static class DebuffsDeMembro
{
	// ============================ OS NUMEROS DO ORIGINAL (o dono ajusta aqui) ============================
	/// <summary>Cada braco perdido tira isto da chance de acertar: `1 - armsLost * 0.25` (`CombatMovement.dm:189`).</summary>
	public const double PontariaPorBraco = 0.25;

	/// <summary>O piso do corte de pontaria: `max(..., 0.1)` (`CombatMovement.dm:189`). Com dois bracos nao se chega nele.</summary>
	public const double PisoDaPontaria = 0.10;

	/// <summary>Quantos bracos perdidos atrasam o soco: `armsLost>=2` (`movement handler.dm:167`).</summary>
	public const int BracosQueAtrasamOSoco = 2;

	/// <summary>
	/// Com que ritmo a recarga do soco escorre sem os dois bracos: `attacking - 0.5` por tique no lugar de
	/// `attacking - 1` (`movement handler.dm:167`). Meio ritmo = o dobro da espera.
	/// </summary>
	public const double RitmoSemOsBracos = 0.5;

	/// <summary>Quantas pernas perdidas tiram a corrida: `if(legsLost)` (`speedy.dm:36` e `:54`, `movement handler.dm:50`).</summary>
	public const int PernasQueTiramACorrida = 1;

	/// <summary>Quantas pernas perdidas prendem o corpo no chao: `legsLost >= 2 &amp;&amp; !flight` (`movement handler.dm:133`).</summary>
	public const int PernasQuePrendemNoChao = 2;

	/// <summary>
	/// QUANTO DO FERIMENTO VIRA LENTIDAO. 1 = o original inteiro (`mobTime *= HP/100`, `movement handler.dm:55`:
	/// com metade da vida, metade do passo). 0 desliga a regra; 0,5 cobra a metade.
	/// </summary>
	public const double PesoDaLentidaoDoFerido = 1;

	/// <summary>
	/// O piso do `hpratio` (`base.dm:76`, `max(HP/100, 0.6)`). **So pro TEXTO**: quem aplica e o
	/// `Fighter.PowerLevel` (`Fighter.Power.cs`), e a bancada `--membrosteste` cobra que os dois concordem.
	/// </summary>
	public const double PisoDoPoderFerido = 0.6;
	// ====================================================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--membrosteste`): nenhum ponto de producao consulta o corpo -- o port de antes
	/// de 2026-10-11, em que perder braco, perna e rabo nao mudava nada. Lido pelas linhas que APLICAM as regras
	/// (a da pontaria no `MeleeResolver.Resolver` e as perguntas do `GameServer.Membros.cs`, a do SSJ4 sem rabo
	/// inclusa); as funcoes daqui continuam respondendo a verdade, e os textos da tela com elas.
	/// </summary>
	public static bool DesligadosDeTeste;

	// =====================================================================
	// O QUE O CORPO PERDEU
	// =====================================================================
	/// <summary>
	/// A CONTA DE UM CORPO: bracos e pernas arrancados (os `armsLost`/`legsLost` do DM) e quantos dos quatro
	/// estao fora da guarda -- quebrados OU arrancados, o `block_limbs_down()` de `BlockLimbs.dm:42-46`.
	///
	/// MAO E PE NAO CONTAM, e isso e do original nos dois lugares: `Injuries.dm:229-230` exclui `Arm/Hand` e
	/// `Leg/Foot` com um `!istype`, e `BlockLimbs.dm:28-31` os tira por serem aninhados. Contar os dois dobraria
	/// a pena de quem perdeu um braco so (aqui o braco leva a mao junto -- ver <see cref="Body.Decepar"/>).
	/// </summary>
	public readonly record struct Perdas(int Bracos, int Pernas, int ForaDaGuarda)
	{
		/// <summary>Nada arrancado nem quebrado entre bracos e pernas.</summary>
		public bool Nenhuma => Bracos == 0 && Pernas == 0 && ForaDaGuarda == 0;
	}

	/// <summary>Braco INTEIRO (nao a mao): o `/datum/Body/Arm` sem o subtipo `Hand`.</summary>
	public static bool EhBraco(BodyPart p) => !p.Aninhado && p.Zona == "bracos";

	/// <summary>Perna INTEIRA (nao o pe): o `/datum/Body/Leg` sem o subtipo `Foot`.</summary>
	public static bool EhPerna(BodyPart p) => !p.Aninhado && p.Zona == "pernas";

	/// <summary>
	/// A conta de um corpo de verdade. Um laco de quinze partes, sem alocar: o soco pergunta isto a cada golpe e
	/// o funil do passo a cada pacote de input.
	/// </summary>
	public static Perdas De(Body corpo)
	{
		int bracos = 0, pernas = 0, fora = 0;
		foreach (BodyPart p in corpo.Partes)
		{
			bool braco = EhBraco(p), perna = EhPerna(p);
			if (!braco && !perna) continue;
			if (p.Decepado) { if (braco) bracos++; else pernas++; }
			if (p.Decepado || p.Quebrado) fora++;
		}
		return new Perdas(bracos, pernas, fora);
	}

	/// <summary>O que cada NOME de membro e -- pro lado que so tem o nome (o cliente recebe `ParteState.Nome`).</summary>
	public enum Tipo : byte { Outro, Braco, Mao, Perna, Pe, Rabo, Nucleo }

	/// <summary>
	/// A TABELA SAI DO PROPRIO <see cref="Body.Novo"/>, e nao de uma lista de nomes escrita aqui: um membro
	/// renomeado la muda de nome aqui junto, em vez de virar um "Outro" calado que ninguem cobra.
	/// </summary>
	private static readonly Dictionary<string, Tipo> _tipos = Classificar();

	private static Dictionary<string, Tipo> Classificar()
	{
		var d = new Dictionary<string, Tipo>();
		Body molde = Body.Novo(comRabo: true);
		foreach (BodyPart p in molde.Partes)
			d[p.Nome] = EhBraco(p) ? Tipo.Braco
					  : EhPerna(p) ? Tipo.Perna
					  : p.Aninhado && p.Zona == "bracos" ? Tipo.Mao
					  : p.Aninhado && p.Zona == "pernas" ? Tipo.Pe
					  : p.Nome == "Rabo" ? Tipo.Rabo
					  : p.Papel == Vitalidade.Nucleo ? Tipo.Nucleo
					  : Tipo.Outro;
		return d;
	}

	public static Tipo TipoDe(string nome) => _tipos.GetValueOrDefault(nome, Tipo.Outro);

	/// <summary>
	/// A MESMA CONTA DO <see cref="De"/>, um membro por vez, pra quem so tem o que veio no fio (nome, vida em
	/// porcento, arrancado). O cliente soma os membros do `S2C.Corpo` com isto e pergunta as mesmas funcoes.
	/// </summary>
	public static Perdas Somar(Perdas ate, string nome, double vidaPct, bool decepado)
	{
		Tipo t = TipoDe(nome);
		if (t is not (Tipo.Braco or Tipo.Perna)) return ate;
		bool quebrado = !decepado && vidaPct < Regras.LimiarQuebra * 100;
		return new Perdas(
			ate.Bracos + (decepado && t == Tipo.Braco ? 1 : 0),
			ate.Pernas + (decepado && t == Tipo.Perna ? 1 : 0),
			ate.ForaDaGuarda + (decepado || quebrado ? 1 : 0));
	}

	// =====================================================================
	// AS CINCO REGRAS
	// =====================================================================
	/// <summary>
	/// QUANTO DA PONTARIA SOBRA -- `if(armsLost) bhit *= max(1 - armsLost * 0.25, 0.1)` (`CombatMovement.dm:189`).
	/// Um braco: 0,75. Os dois: 0,50. O piso de 0,1 so existiria num corpo de quatro bracos.
	///
	/// VALE PRA TODO CORPO, NPC incluso: a linha mora no `hitProc`, que o DM divide entre jogador e NPC, e o
	/// `HealthSync` reconta os bracos de qualquer mob com alvo (`Injuries.dm:217`).
	/// </summary>
	public static double FatorDePontaria(Perdas p, double escala = 1) =>
		Escalar(p.Bracos <= 0 ? 1 : Math.Max(1 - p.Bracos * PontariaPorBraco, PisoDaPontaria), escala);

	/// <summary>
	/// COM QUE RITMO A RECARGA DO SOCO ESCORRE -- `attacking = max(0, attacking - (armsLost>=2 ? 0.5 : 1))`
	/// (`movement handler.dm:167`). 1 = normal; 0,5 = o soco leva o dobro pra voltar. Um braco so nao muda nada.
	/// </summary>
	public static double RitmoDaRecarga(Perdas p, double escala = 1) =>
		Escalar(p.Bracos >= BracosQueAtrasamOSoco ? RitmoSemOsBracos : 1, escala);

	/// <summary>
	/// (v0.4.0) A ESCALA RACIAL DE UM DEBUFF DE MEMBRO: o que o fator TIRA, vezes a escala -- 0,5 tira metade (Warrior's Vitality), 1,1
	/// tira 10% a mais (o Primitive Rage do Demonic Saiyan), 0 nao tira nada (sem efeitos negativos, `CombatState.EscalaDosMembros`).
	/// So os fatores CONTINUOS escalam: sem as pernas o corpo continua sem correr e sem voar.
	/// </summary>
	public static double Escalar(double fator, double escala) => Math.Clamp(1 - (1 - fator) * Math.Max(escala, 0), 0.01, 1);

	/// <summary>
	/// ESTE CORPO CORRE? -- `if(legsLost) { "You can't dash with a leg torn off!"; return }` (`speedy.dm:36-38`,
	/// e o `SttDash` da tecla em `:54`), mais `if(dashing &amp;&amp; !legsLost) mobTime += 0.5*Epspeed`
	/// (`movement handler.dm:50`): quem ja vinha correndo perde o embalo na hora. UMA perna basta.
	///
	/// So a corrida NO CHAO: o SHIFT no ar e o superflight, e o DM nao pergunta pelas pernas la (`:52-53`).
	/// </summary>
	public static bool Corre(Perdas p) => p.Pernas < PernasQueTiramACorrida;

	/// <summary>
	/// ESTE CORPO SAI DO LUGAR A PE? -- `if(legsLost >= 2 &amp;&amp; !flight) mobTime = 0 //sem as 2 pernas so da pra se
	/// mover voando` (`movement handler.dm:133`). Voando ele anda como sempre; quem pergunta pelo voo e o servidor.
	/// </summary>
	public static bool AndaNoChao(Perdas p) => p.Pernas < PernasQuePrendemNoChao;

	/// <summary>
	/// `undelayed = 1`: o Androide (`statandroid.dm:2`) e o Majin (`statmajin.dm:14`) nao mancam -- e o bio com o sangue de um
	/// deles (v0.3.0): a pergunta recebe a RACA COM SANGUE (`Races.SangueDoBio`).
	/// </summary>
	public static bool SemAtrasoDeFerimento(string raca) =>
		Races.SangueDoBio.Tem(raca, "Android") || Races.SangueDoBio.Tem(raca, "Majin");

	/// <summary>
	/// QUANTO DO PASSO SOBRA A UM CORPO FERIDO -- `if(HP>0&amp;&amp;!undelayed) mobTime*=HP/100 //Damage delay`
	/// (`movement handler.dm:55`). A vida e a MEDIA dos membros que restam (o arrancado sai da conta, ver
	/// <see cref="Body.Vida"/>), entao perder o braco nao freia ninguem -- o que freia e o estrago no que sobrou.
	///
	/// Devolve o FATOR DE VIDA do acumulador, e nao a velocidade: quem o encaixa na ordem do original (antes do
	/// peso, antes do esmagamento, com o piso de `mobTime` no fim) e o `Esmagamento.FatorDePasso`.
	/// </summary>
	public static double FatorDoPasso(double hp, string raca, double escala = 1)
	{
		if (hp <= 0 || SemAtrasoDeFerimento(raca)) return 1;
		return Escalar(1 - (1 - Math.Min(hp / 100, 1)) * PesoDaLentidaoDoFerido, escala);
	}

	/// <summary>O `hpratio` (`base.dm:76`) -- ver <see cref="PisoDoPoderFerido"/>. So a tela usa isto.</summary>
	public static double FatorDoPoder(double hp) => Math.Max(Math.Min(hp / 100, 1), PisoDoPoderFerido);

	// =====================================================================
	// O QUE A TELA DIZ
	// =====================================================================
	/// <summary>O nome do fio (sem acento, a chave do corpo) -> o nome que a tela escreve.</summary>
	public static string NomeNaTela(string nome) => nome switch
	{
		"Cabeca" => "Cabeça",
		"Cerebro" => "Cérebro",
		"Abdomen" => "Abdômen",
		"Orgaos" => "Órgãos",
		"Braco esquerdo" => "Braço esquerdo",
		"Braco direito" => "Braço direito",
		"Mao esquerda" => "Mão esquerda",
		"Mao direita" => "Mão direita",
		"Pe esquerdo" => "Pé esquerdo",
		"Pe direito" => "Pé direito",
		_ => nome,
	};

	private static bool Feminino(string nome) =>
		nome.StartsWith("Perna", StringComparison.Ordinal) || nome.StartsWith("Mao", StringComparison.Ordinal)
		|| nome == "Cabeca";

	private static string Pct(double fracao) => $"{fracao * 100:0}%";

	private static string Vezes(double x) => $"{x:0.#}×";

	/// <summary>
	/// A LINHA DE UM MEMBRO na aba Body: o que o estado DELE custa. Nula = nada a dizer (membro inteiro, ou um
	/// que nao cobra nada por si).
	/// </summary>
	public static string? NotaDoMembro(string nome, double vidaPct, bool decepado)
	{
		Tipo t = TipoDe(nome);
		bool quebrado = !decepado && vidaPct < Regras.LimiarQuebra * 100;
		string guarda = $"a guarda cede {MeleeResolver.FalhaDaGuardaPorMembro:0}% mais";
		return t switch
		{
			Tipo.Braco when decepado =>
				$"golpes com {Pct(PontariaPorBraco)} menos chance de acertar · não apara mais com ele · {guarda}",
			Tipo.Braco when quebrado => $"quebrado: não apara golpes com ele · {guarda}",
			Tipo.Perna when decepado => $"sem ela você não corre · não apara mais com ela · {guarda}",
			Tipo.Perna when quebrado => $"quebrada: não apara golpes com ela · {guarda}",
			Tipo.Mao when decepado => "foi junto com o braço",
			Tipo.Pe when decepado => "foi junto com a perna",
			Tipo.Rabo when decepado => "sem ele não há Oozaru · o treino passa a render mais",
			Tipo.Nucleo when quebrado =>
				$"abaixo de {Pct(Regras.LimiarQuebra)} o corpo desliga: nocaute até sarar",
			_ => null,
		};
	}

	/// <summary>
	/// TUDO O QUE ESTA VALENDO NESTE CORPO AGORA, uma frase por efeito -- o cartao "Efeitos" da aba Body. Vazia =
	/// corpo sem pena nenhuma.
	/// </summary>
	/// <param name="hp">A vida inteira (a media dos membros), 0-100.</param>
	/// <param name="raca">A chave da raca: Androide e Majin nao mancam (<see cref="SemAtrasoDeFerimento"/>).</param>
	public static List<string> EmVigor(Perdas p, double hp, string raca)
	{
		var linhas = new List<string>();

		if (FatorDePontaria(p) < 1)
			linhas.Add($"Seus golpes têm {Pct(1 - FatorDePontaria(p))} menos chance de acertar "
					 + $"({Quantos(p.Bracos, "braço perdido", "braços perdidos")}).");
		if (RitmoDaRecarga(p) < 1)
			linhas.Add($"A recarga do soco está {Vezes(1 / RitmoDaRecarga(p))} mais lenta (sem os dois braços).");
		if (!AndaNoChao(p))
			linhas.Add("No chão você não sai do lugar: só voando (sem as duas pernas).");
		else if (!Corre(p))
			linhas.Add("Você não corre (perna perdida).");

		if (p.ForaDaGuarda >= MembrosDaGuarda)
			linhas.Add("Você não ergue a guarda: não sobrou braço nem perna inteiro.");
		else if (p.ForaDaGuarda > 0)
			linhas.Add($"A guarda tem {p.ForaDaGuarda * MeleeResolver.FalhaDaGuardaPorMembro:0}% a mais de chance de ceder "
					 + $"({Quantos(p.ForaDaGuarda, "braço ou perna fora de combate", "braços e pernas fora de combate")}).");

		double passo = FatorDoPasso(hp, raca);
		if (passo < 0.995)
			linhas.Add($"Seu passo está {Pct(1 - passo)} mais lento (corpo ferido: vida em {hp:0}%).");
		double poder = FatorDoPoder(hp);
		if (hp > 0 && poder < 0.995)
			linhas.Add($"Seu poder rende {Pct(1 - poder)} a menos enquanto o corpo estiver ferido.");

		return linhas;
	}

	/// <summary>Os quatro membros que aparam: dois bracos e duas pernas (<see cref="Body.Novo"/>).</summary>
	public const int MembrosDaGuarda = 4;

	private static string Quantos(int n, string um, string varios) => n == 1 ? $"1 {um}" : $"{n} {varios}";

	/// <summary>
	/// A FRASE DO CHAT NO INSTANTE DA PERDA, pra quem perdeu: o que caiu e o que passa a valer -- com os numeros
	/// de DEPOIS da perda (<paramref name="depois"/>), porque o segundo braco nao custa o mesmo que o primeiro.
	/// </summary>
	public static string FraseDaPerda(string nome, Perdas depois)
	{
		string membro = Minuscula(NomeNaTela(nome));
		string caiu = Feminino(nome) ? $"sua {membro} foi ARRANCADA!" : $"seu {membro} foi ARRANCADO!";
		return TipoDe(nome) switch
		{
			Tipo.Braco when RitmoDaRecarga(depois) < 1 =>
				$"{caiu} Sem os dois braços seus golpes têm {Pct(1 - FatorDePontaria(depois))} menos chance de acertar "
				+ $"e a recarga do soco fica {Vezes(1 / RitmoDaRecarga(depois))} mais lenta.",
			Tipo.Braco =>
				$"{caiu} Seus golpes passam a ter {Pct(1 - FatorDePontaria(depois))} menos chance de acertar, "
				+ "e você não apara mais com ele.",
			Tipo.Perna when !AndaNoChao(depois) =>
				$"{caiu} Sem as duas pernas você não sai do lugar no chão: só voando.",
			Tipo.Perna => $"{caiu} Você não corre mais, e não apara mais com ela.",
			Tipo.Rabo => $"{caiu} Sem ele não há Oozaru, e o seu treino passa a render mais.",
			_ => caiu,
		};
	}

	/// <summary>A frase pra quem ASSISTE: o `[savant]'s [src] was torn off!` de `mobparts_logic.dm:109`.</summary>
	public static string FraseDaPerdaAlheia(string dono, string nome)
	{
		string membro = Minuscula(NomeNaTela(nome));
		return Feminino(nome) ? $"a {membro} de {dono} foi ARRANCADA!" : $"o {membro} de {dono} foi ARRANCADO!";
	}

	/// <summary>O que o SHIFT responde a quem tenta correr sem perna -- o `You can't dash with a leg torn off!` (`speedy.dm:37`).</summary>
	public const string AvisoDeCorrida = "você não consegue correr com uma perna arrancada.";

	/// <summary>
	/// O que o ALT responde a quem nao tem braco nem perna inteiro -- o `Seus membros estao quebrados demais para
	/// erguer a guarda!` do `holdblock()` (`CombatMovement.dm:78`).
	/// </summary>
	public const string AvisoDeGuarda = "seus membros estão quebrados demais pra erguer a guarda!";

	/// <summary>O que o corpo responde a quem tenta andar sem as duas pernas (`movement handler.dm:133`).</summary>
	public const string AvisoDeChao = "sem as duas pernas você não sai do lugar no chão: levante voo pra se mover.";

	private static string Minuscula(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
