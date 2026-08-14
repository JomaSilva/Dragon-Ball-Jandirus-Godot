using Jandirus.Core.Combat;

namespace Jandirus.Core.Skills;

/// <summary>
/// O QUE SE PODE COMPRAR NA MESA -- uma entrada por BOTAO do painel do DM
/// (`customattacks.dm:937-1400`), e so o que MEXE EM PONTO entra aqui.
///
/// Os gritos, as auras e os dois interruptores de fala sao livres no original
/// (`ShoutBeforeCheckbox`, `ShoutAfterCheckbox`, `ChargeAuraCheckbox`, `AttackAuraCheckbox` so
/// escrevem a flag e chamam `UpdateCustomAttack`). Junta-los aqui daria a impressao de que custam
/// alguma coisa, e a mesa passaria a ter dois tipos de "compra" com o mesmo nome.
/// </summary>
public enum Compra : byte
{
	DanoMais, DanoMenos,
	CargaMais, CargaMenos,
	KiMais, KiMenos,
	StaminaLigar, StaminaDesligar, StaminaMais, StaminaMenos,
	VelocidadeMais, VelocidadeMenos,
	InstantaneoLigar, InstantaneoDesligar,
	CarregavelLigar, CarregavelDesligar,

	/// <summary>Alcance em TILES. Pede argumento (o valor desejado), como o `input` do DM.</summary>
	Alcance,

	/// <summary>`rangemodifier`. Pede argumento. Cada 0,1 e um ponto -- ver a nota da tabela.</summary>
	DistanciaMod,
}

/// <summary>
/// UMA TECNICA DE KI FEITA PELO JOGADOR -- o `/datum/skill/CustomAttack` do DM
/// (`Code/Modules/Skills/CustomAttacks/customattacks.dm`), inteiro e puro.
///
/// ============================ ISTO E PORTE, E OS NUMEROS SAO DE LA ============================
/// Nada aqui foi escolhido: cada padrao, cada piso, cada preco em ponto e cada guarda esta citada
/// linha a linha contra o original. Onde este arquivo DIVERGE, o comentario diz onde e por que --
/// sao tres lugares, todos guardas que o DM esqueceu de fechar.
///
/// MAIS UMA, E ESSA E DECISAO DO DONO E NAO CONSERTO DE PORTE: o `custompoints_spent` tem PISO EM
/// ZERO. No DM ele fica negativo e o orcamento de 5 vira elastico; aqui nao. A conta inteira -- o
/// que se perdeu, o que se ganhou, e por que a desvantagem sem saldo e RECUSADA em vez de perdida
/// -- esta em <see cref="Gasto"/> e em <see cref="Cobrar"/>. Leia os dois antes de "consertar" isto
/// de volta pro DM.
/// ==========================================================================================
///
/// ============================ A MESA EDITA UMA COPIA ============================
/// O DM tem VINTE E QUATRO variaveis-sombra (`customattack_*`, `:26-51`) que existem so pra o
/// painel poder mexer sem estragar a tecnica de pe: `GenerateCustomAttackStats` copia real->sombra
/// ao abrir, e `CreateCustomSkill` copia sombra->real no "Done". Cancelar simplesmente joga a
/// sombra fora.
///
/// Aqui a sombra e uma COPIA DESTA MESMA CLASSE (ver <see cref="Clonar"/>): a mesa e uma
/// `TecnicaCustomizada` solta, e salvar e um `CopiarDe`. Vinte e quatro campos duplicados sao
/// vinte e quatro chances de esquecer um -- e o proprio DM esqueceu: `S.desc` e lido do widget e
/// nao da sombra, `attackcounter` e `expbuffer` nunca sao copiados de volta.
/// ================================================================================
/// </summary>
public sealed class TecnicaCustomizada
{
	// =====================================================================
	// OS TETOS
	// =====================================================================
	/// <summary>
	/// DEZ TECNICAS, ids 1..10. E o `switch(usr.customattacks.len)` do `createcustomizableskill`
	/// (`:276-318`), que so tem ramo pra dez -- com dez na mao ele nao casa com nenhum `if` e a
	/// criacao vira uma chamada que nao faz nada.
	///
	/// Aqui o teto RECUSA COM MOTIVO em vez de sair calado. "Silencio no lugar de erro" e a
	/// armadilha 5 da casa, e o `switch` sem `else` do original e o exemplo perfeito dela: o
	/// jogador aperta Create, nao acontece nada, e nao ha como saber se travou ou se ele ja tinha
	/// dez.
	/// </summary>
	public const int Maximo = 10;

	/// <summary>`total_custompoints = 5` (`:87`). O ORCAMENTO LIQUIDO -- ver <see cref="Gasto"/>.</summary>
	public const int PontosTotais = 5;

	// =====================================================================
	// OS PADROES (`:77-92`)
	// =====================================================================
	public const double DanoPadrao = 0.8;        // `base_damage = 0.8`
	public const double CargaPadrao = 1;         // `minimum_chargetime = 1`
	public const double KiPadrao = 20;           // `ki_cost = 20`
	public const double VelocidadePadrao = 1;    // `speed = 1`
	public const double AlcancePadrao = 20;      // `range = 20`
	public const double DistModPadrao = 1;       // `rangemodifier = 1`

	// =====================================================================
	// OS PISOS E TETOS DE CADA CAMPO -- cada um de uma guarda do DM
	// =====================================================================
	public const double DanoPiso = 0.1;          // `base_damage - 0.1 >= 0.1` (`:972`)
	public const double DanoPasso = 0.1;
	public const double CargaPiso = 0.2;         // `minimum_chargetime - 0.4 >= 0.2` (`:947`)
	public const double CargaPasso = 0.4;
	public const double KiPiso = 20;             // `ki_cost - 40 >= 20` (`:990`)
	public const double KiPasso = 40;
	public const double StaminaPiso = 1;         // `stamina_cost - 1 >= 1` (`:1031`)
	public const double VelocidadePiso = 0.2;    // `speed - 0.2 >= 0.2` (`:1064`)
	public const double VelocidadeTeto = 5;      // `speed + 1 <= 5` (`:1046`)
	public const double VelocidadeMiudo = 0.2;   // o passo FINO, abaixo de 1 (`:1056`)
	public const int AlcancePiso = 5;            // `amount >= 5` (`:1347`)
	public const double DistModPiso = 0.5;       // `amount >= 0.5` (`:1378`)

	/// <summary>O `instantattack` custa 2 (`:1319`), e ligar a estamina devolve 2 (`:1008`).</summary>
	public const int PrecoDoInstantaneo = 2;
	public const int PrecoDaEstamina = 2;

	/// <summary>
	/// QUANTOS PONTOS UM SEGUNDO DE CARGA VALE, na conta do proprio DM: o estorno de desligar a
	/// carga e `(chargetime - 1) * 2.5` (`:922`), e como cada passo e de 0,4 s, `0,4 * 2,5 = 1`.
	/// Ou seja o numero magico 2,5 do original E o inverso do passo -- e por isso ele mora aqui
	/// derivado, e nao digitado: mexer no passo sem mexer no 2,5 faria o estorno divergir da compra
	/// e o jogador ganharia (ou perderia) ponto ao ligar e desligar a caixinha.
	/// </summary>
	public const double PontosPorSegundoDeCarga = 1 / CargaPasso;

	// =====================================================================
	// A TECNICA
	// =====================================================================
	/// <summary>1..10. E ele que vira o verbo `Custom_Attack&lt;id&gt;` (`:175-213`).</summary>
	public int Id;

	/// <summary>
	/// `attacktype`. Os bytes sao os mesmos do DM (0 Beam, 1 Blast, 2 Guided) porque o
	/// <see cref="TipoDeProjetil"/> da camada do projetil ja os herdou -- e o Melee (3) nao existe
	/// nos dois pelo mesmo motivo: tem tela de montagem e nao tem disparo.
	/// </summary>
	public TipoDeProjetil Tipo = TipoDeProjetil.Beam;

	public string Nome = "Custom Attack";        // `name` (`:95`)
	public string Desc = "Add a description here.";
	public string Grito = "RAAAAGH!!";           // `shout` (`:75`)
	public string GritoDeCarga = "Take this....!";
	public bool DizGrito;                        // `doshout` -- LIVRE, nao custa ponto
	public bool DizGritoDeCarga;                 // `dochargeshout` -- idem

	public double BaseDano = DanoPadrao;
	public double CargaMinima = CargaPadrao;
	public double CustoKi = KiPadrao;
	public double CustoStamina;                  // `stamina_cost = 0`
	public bool UsaStamina;                      // `use_stamina = 0`
	public bool Carregavel;                      // `chargeattack` -- ver `PorTipo`
	public double Velocidade = VelocidadePadrao;
	public double Alcance = AlcancePadrao;
	public double DistanciaMod = DistModPadrao;
	public bool Instantaneo;                     // `instantattack = 0`

	/// <summary>
	/// `custompoints_spent`, PRESO ENTRE 0 E <see cref="PontosTotais"/> -- e este piso e a UNICA
	/// divergencia de REGRA em relacao ao original (as outras tres sao guardas que o DM esqueceu de
	/// fechar, nao mudancas de economia).
	///
	/// ============================ O DONO CORTOU O SALDO NEGATIVO ============================
	/// No DM `custompoints_spent` fica NEGATIVO: rebaixar dano, carga, Ki ou velocidade estorna
	/// ponto e o estorno nao tem fundo, entao quem levava o dano de 0,8 ao piso de 0,1 recebia 7 e
	/// passava a ter DOZE pontos pra gastar numa tecnica de cinco. Este porte carregou isso por um
	/// tempo, com um comentario aqui defendendo que "era a economia do original".
	///
	/// O DONO DECIDIU CORTAR, e o que se perde e o que se ganha estao os dois escritos aqui pra
	/// ninguem "consertar" isto de volta daqui a tres meses:
	///
	///   PERDEU-SE  a compensacao ILIMITADA -- a tecnica horrorosa de proposito (dano no chao, carga
	///              eterna, Ki caro) que financiava um orcamento enorme em cima do que sobrasse. Com
	///              o piso, azucrinar a propria tecnica so devolve o que ela ja custou.
	///   GANHOU-SE  o "5" de volta como TETO. Antes ele era um saldo liquido -- um numero que dizia
	///              quanto se tinha gasto A MAIS do que se tinha rendido, e do qual nao se conseguia
	///              deduzir teto de poder nenhum. Agora <c>0 &lt;= Gasto &lt;= 5</c> em todo instante,
	///              e "5 pontos" quer dizer cinco vantagens, ponto -- que e o que a tela sempre
	///              prometeu e o codigo nao cumpria.
	///
	/// A DESVANTAGEM COM O GASTO EM ZERO E RECUSADA, e nao perdida -- ver <see cref="Cobrar"/>.
	/// =====================================================================================
	///
	/// O `private set` nao e enfeite: e ele que obriga toda compra a passar pelo funil. Ver
	/// <see cref="Cobrar"/> e <see cref="RestaurarGasto"/>.
	/// </summary>
	[System.Text.Json.Serialization.JsonInclude]
	public int Gasto { get; private set; }

	/// <summary>
	/// REESCREVE O GASTO SEM COBRAR NADA. So pra quem RECONSTROI uma tecnica que ja foi conferida:
	/// o fio (`CustomWire.Ler`), o disco (o `System.Text.Json` do save) e o <see cref="CopiarDe"/>.
	///
	/// GRAMPEIA no intervalo, e isso faz as vezes de migracao: o save escrito antes do piso pode ter
	/// `Gasto` negativo, e carrega-lo cru daria de volta o orcamento inflado que o dono cortou. A
	/// tecnica antiga fica com os numeros que ela tinha (ninguem perde a tecnica); o que volta ao
	/// lugar e so o saldo. Grampear num lugar so tambem e o que impede um pacote adulterado de
	/// chegar com -300 e abrir 305 pontos de compra.
	/// </summary>
	public void RestaurarGasto(int g) => Gasto = Math.Clamp(g, 0, PontosTotais);

	/// <summary>`created`: ja foi confirmada uma vez. Falso = ainda e rascunho na mesa.</summary>
	public bool Criada;

	/// <summary>
	/// Quantos pontos ainda cabem. `custompoints_left` (`:644`).
	///
	/// Com o piso em zero ele vive em 0..5 -- antes podia passar de 5 (era o saldo inflado) e a tela
	/// tinha que pintar de vermelho um numero negativo que nunca aparecia.
	/// </summary>
	public int Restantes => PontosTotais - Gasto;

	/// <summary>O nome do verbo, do jeito que o servidor e o cliente falam dele.</summary>
	public string Verbo => "Custom_Attack" + Id;

	/// <summary>Prefixo dos verbos custom. Existe pra ninguem escrever a string a mao em dois lugares.</summary>
	public const string PrefixoDoVerbo = "Custom_Attack";

	/// <summary>
	/// O id embutido num verbo `Custom_Attack7`, ou 0 se nao for um deles. E a porta de entrada do
	/// despacho no servidor E do botao no cliente -- uma so, pelo mesmo motivo da regra 4 da casa.
	/// </summary>
	public static int IdDoVerbo(string verbo)
	{
		if (!verbo.StartsWith(PrefixoDoVerbo, StringComparison.OrdinalIgnoreCase)) return 0;
		return int.TryParse(verbo.AsSpan(PrefixoDoVerbo.Length), out int n) && n >= 1 && n <= Maximo
			? n : 0;
	}

	// =====================================================================
	// O ENCAIXE COM A CAMADA DO PROJETIL
	// =====================================================================
	/// <summary>
	/// A RECEITA QUE VOA. Esta e a razao de a camada 1 ter nascido com
	/// <see cref="ReceitaDeProjetil"/>: a tecnica do jogador e a tecnica portada a mao entram pela
	/// MESMA porta (`GameServer.Disparar`), e por isso existe um projetil e nao dois.
	///
	/// O que NAO viaja: `MaxDano`, `Fisico`, `Deflectivel` e `Piercer` ficam no padrao porque o
	/// painel do DM nao os oferece -- eles sao de tecnica escrita a mao (`objects.dm`), e inventar
	/// um botao pra eles aqui seria acrescentar poder que o original nao vende.
	/// </summary>
	public ReceitaDeProjetil Receita() => new()
	{
		Tipo = Tipo,
		BaseDano = BaseDano,
		Velocidade = Velocidade,
		AlcanceTiles = Alcance,
		RangeMod = DistanciaMod,
		CargaMinima = CargaMinima,
		Instantaneo = Instantaneo,
		Nome = Nome,
	};

	// =====================================================================
	// A MESA
	// =====================================================================
	public TecnicaCustomizada Clonar() => (TecnicaCustomizada)MemberwiseClone();

	/// <summary>Finaliza: a sombra vira a tecnica de pe. O `CreateCustomSkill` do DM (`:1211-1229`).</summary>
	public void CopiarDe(TecnicaCustomizada m)
	{
		Tipo = m.Tipo;
		Nome = m.Nome; Desc = m.Desc; Grito = m.Grito; GritoDeCarga = m.GritoDeCarga;
		DizGrito = m.DizGrito; DizGritoDeCarga = m.DizGritoDeCarga;
		BaseDano = m.BaseDano; CargaMinima = m.CargaMinima;
		CustoKi = m.CustoKi; CustoStamina = m.CustoStamina; UsaStamina = m.UsaStamina;
		Carregavel = m.Carregavel; Velocidade = m.Velocidade;
		Alcance = m.Alcance; DistanciaMod = m.DistanciaMod; Instantaneo = m.Instantaneo;
		RestaurarGasto(m.Gasto);
	}

	/// <summary>
	/// ESCOLHER O TIPO -- e ele decide sozinho se a tecnica CARREGA.
	///
	/// `PickAttackType` (`:873-902`) faz `chargeattack = 1` pro Beam e `= 0` pros outros dois, e
	/// logo em seguida `InitBeam` DESABILITA a caixinha de carga. Ou seja: no original o raio
	/// sempre carrega e a bola nunca carrega, e a caixinha so e clicavel em Blast/Guided -- onde
	/// ela liga uma carga que o `CustomBlastFire` nem le. Aqui isso vira o que ela sempre foi na
	/// pratica: uma consequencia do tipo.
	/// </summary>
	public void PorTipo(TipoDeProjetil t)
	{
		Tipo = t;
		Carregavel = t == TipoDeProjetil.Beam;
	}

	/// <summary>Modificadores de raio (`AddModifiers`, `:1306`) so existem no Beam.</summary>
	public bool AceitaModificadoresDeRaio => Tipo == TipoDeProjetil.Beam;

	// =====================================================================
	// A LOJA DE PONTOS -- o funil unico
	// =====================================================================
	/// <summary>
	/// COMPRAR (ou estornar) UMA COISA. Devolve falso com o motivo do proprio DM quando a guarda
	/// recusa.
	///
	/// ============================ UM FUNIL SO, E ELE E A TABELA ============================
	/// No original cada botao e um `verb` com a sua propria copia da guarda -- dezoito copias de
	/// `if (spent + 1 <= total)`. Foi assim que tres delas ficaram diferentes das outras quinze
	/// (ver as tres divergencias marcadas abaixo). Aqui ha um lugar so, e a bancada o percorre
	/// inteiro.
	///
	/// E O TETO E O PISO MORAM NO MESMO LUGAR: nenhum `case` daqui escreve em <see cref="Gasto"/> --
	/// todos chamam <see cref="Cobrar"/>, que confere as duas bordas e so entao escreve. Por isso o
	/// `case` novo de amanha ja nasce guardado: ele nem CONSEGUE mexer no saldo por fora.
	/// ==================================================================================
	/// </summary>
	public bool Aplicar(Compra c, double arg, out string porque)
	{
		porque = "";
		switch (c)
		{
			// ---------------------------------------------------------- dano (`:958`, `:969`)
			case Compra.DanoMais:
				if (!Cobrar(1, out porque)) return false;
				BaseDano = Arredonda(BaseDano + DanoPasso);
				return true;

			case Compra.DanoMenos:
				// No original nao ha guarda de ponto aqui -- o estorno so ENGORDAVA o saldo. Com o
				// piso do dono, `Cobrar(-1)` e quem recusa quando nao ha o que estornar.
				if (Arredonda(BaseDano - DanoPasso) < DanoPiso)
				{ porque = "voce nao pode baixar mais a potencia."; return false; }
				if (!Cobrar(-1, out porque)) return false;
				BaseDano = Arredonda(BaseDano - DanoPasso);
				return true;

			// ---------------------------------------------------------- carga (`:937`, `:944`)
			case Compra.CargaMais:
				if (!Carregavel) { porque = "so um raio carrega."; return false; }
				if (!Cobrar(-1, out porque)) return false;
				CargaMinima = Arredonda(CargaMinima + CargaPasso);
				return true;

			case Compra.CargaMenos:
				if (!Carregavel) { porque = "so um raio carrega."; return false; }
				if (Arredonda(CargaMinima - CargaPasso) < CargaPiso)
				{ porque = "voce nao pode encurtar mais a carga."; return false; }
				if (!Cobrar(1, out porque)) return false;
				CargaMinima = Arredonda(CargaMinima - CargaPasso);
				return true;

			// ---------------------------------------------------------- Ki (`:980`, `:987`)
			case Compra.KiMais:
				if (!Cobrar(-1, out porque)) return false;
				CustoKi += KiPasso;
				return true;

			case Compra.KiMenos:
				// COM O PADRAO DE 20 ISTO NUNCA PASSA (`20 - 40 = -20`), e e assim la tambem: pra
				// baratear o Ki e preciso ter encarecido antes. O piso e o padrao.
				if (CustoKi - KiPasso < KiPiso)
				{ porque = "voce nao pode baratear mais o custo de energia."; return false; }
				if (!Cobrar(1, out porque)) return false;
				CustoKi -= KiPasso;
				return true;

			// ---------------------------------------------------------- estamina (`:1001`, `:1021`)
			// LIGAR A ESTAMINA DEVOLVE 2 PONTOS -- `custompoints_spent += -2` (`:1008`).
			//
			// Vale sublinhar porque a leitura oposta e natural e esta errada: pagar estamina ALEM do
			// Ki e uma DESVANTAGEM, e o painel paga por ela. A caixinha nao "custa 2 pontos"; ela
			// RENDE 2, e desliga-la e que cobra os 2 de volta mais o que os degraus de estamina
			// tinham rendido.
			case Compra.StaminaLigar:
				if (UsaStamina) { porque = "essa tecnica ja gasta folego."; return false; }
				if (!Cobrar(-PrecoDaEstamina, out porque)) return false;
				UsaStamina = true;
				CustoStamina = StaminaPiso;
				return true;

			case Compra.StaminaDesligar:
			{
				if (!UsaStamina) { porque = "essa tecnica nao gasta folego."; return false; }
				// `refund_difference = stamina_cost - 1`, e cobra-se `refund + 2` (`:1005`, `:1013`)
				int cobra = (int)(CustoStamina - StaminaPiso) + PrecoDaEstamina;
				// ============ DIVERGENCIA 1 DE 3: A GUARDA DO DM CONFERE O NUMERO ERRADO ============
				// La o `if` testa `refund_difference + spent <= total` e a linha seguinte soma
				// `refund_difference + 2`. Sobram 2 pontos por fora do teto -- o mesmo defeito que a
				// camada do projetil ja consertou no `Guided_Ball` (confere 50, cobra 600).
				// Aqui a conferencia e do valor que se cobra. Perde-se um furo; ganha-se um teto que
				// e teto.
				// ====================================================================================
				if (!Cobrar(cobra, out porque)) return false;
				UsaStamina = false;
				CustoStamina = 0;
				return true;
			}

			case Compra.StaminaMais:
				if (!UsaStamina) { porque = "ligue o gasto de folego primeiro."; return false; }
				if (!Cobrar(-1, out porque)) return false;
				CustoStamina += 1;
				return true;

			case Compra.StaminaMenos:
				if (!UsaStamina) { porque = "ligue o gasto de folego primeiro."; return false; }
				if (CustoStamina - 1 < StaminaPiso)
				{ porque = "voce nao pode baixar mais o gasto de folego."; return false; }
				if (!Cobrar(1, out porque)) return false;
				CustoStamina -= 1;
				return true;

			// ---------------------------------------------------------- velocidade (`:1042`, `:1060`)
			// DOIS PASSOS, e o degrau muda no 1: de 1 pra cima anda de 1 em 1 ate 5; de 1 pra baixo
			// anda de 0,2 em 0,2 ate 0,2. Logo eles NAO sao inversos exatamente em 1 (subir leva a 2,
			// descer leva a 0,8), e isso e do original.
			case Compra.VelocidadeMais:
			{
				double passo = Velocidade >= 1 ? 1 : VelocidadeMiudo;
				if (Arredonda(Velocidade + passo) > VelocidadeTeto)
				{ porque = $"a velocidade nao passa de {VelocidadeTeto:0}."; return false; }
				// ============ DIVERGENCIA 2 DE 3: O RAMO FINO NAO TINHA GUARDA NENHUMA ============
				// `else { speed += 0.2; spent += 1 }` (`:1056`) -- sem `if`. Quem tivesse descido a
				// velocidade abaixo de 1 subia de volta de graca e ESTOUAVA o teto de pontos.
				// A guarda entra aqui pros dois ramos: o teto tem que valer no caminho todo.
				// =================================================================================
				if (!Cobrar(1, out porque)) return false;
				Velocidade = Arredonda(Velocidade + passo);
				return true;
			}

			case Compra.VelocidadeMenos:
			{
				double passo = Velocidade <= 1 ? VelocidadeMiudo : 1;
				if (Arredonda(Velocidade - passo) < VelocidadePiso)
				{ porque = "voce nao pode deixar o ataque mais lento."; return false; }
				if (!Cobrar(-1, out porque)) return false;
				Velocidade = Arredonda(Velocidade - passo);
				return true;
			}

			// ---------------------------------------------------------- instantaneo (`:1314`)
			case Compra.InstantaneoLigar:
				if (!AceitaModificadoresDeRaio)
				{ porque = "so um raio tem o que adiantar -- bola e teleguiado ja saem na hora."; return false; }
				if (Instantaneo) { porque = "essa tecnica ja sai assim que carrega."; return false; }
				if (!Cobrar(PrecoDoInstantaneo, out porque)) return false;
				Instantaneo = true;
				return true;

			case Compra.InstantaneoDesligar:
				if (!Instantaneo) { porque = "essa tecnica ja espera o dedo soltar."; return false; }
				if (!Cobrar(-PrecoDoInstantaneo, out porque)) return false;
				Instantaneo = false;
				return true;

			// ---------------------------------------------------------- carrega ou nao (`:918`)
			// No DM esta caixinha e clicavel so fora do Beam -- e la ela liga uma carga que o
			// disparo de bola nem le. Fica portada pela regra da casa (regra escrita e regra
			// ligada), mas amarrada ao tipo por `PorTipo`: quem tenta mexer ouve o motivo.
			case Compra.CarregavelLigar:
				if (Carregavel) { porque = "essa tecnica ja carrega."; return false; }
				if (Tipo != TipoDeProjetil.Beam)
				{ porque = "so um raio carrega."; return false; }
				if (!Cobrar(-1, out porque)) return false;
				Carregavel = true;
				return true;

			case Compra.CarregavelDesligar:
			{
				if (!Carregavel) { porque = "essa tecnica ja nao carrega."; return false; }
				// `refund_difference = (chargetime - 1) * 2.5`, e cobra-se `refund + 1` (`:922`, `:929`)
				int cobra = (int)Math.Round((CargaMinima - CargaPadrao) * PontosPorSegundoDeCarga,
											MidpointRounding.AwayFromZero) + 1;
				// DIVERGENCIA 3 DE 3: mesma da estamina -- la a guarda testa `refund` e cobra
				// `refund + 1`. Aqui confere-se o que se cobra.
				if (!Cobrar(cobra, out porque)) return false;
				Carregavel = false;
				CargaMinima = CargaPadrao;
				return true;
			}

			// ---------------------------------------------------------- alcance (`:1339`)
			// UM PONTO POR TILE, nos dois sentidos: `custompointadder = round(amount - range)`.
			case Compra.Alcance:
			{
				if (!AceitaModificadoresDeRaio)
				{ porque = "so o raio tem alcance ajustavel."; return false; }
				int quer = (int)Math.Round(arg, MidpointRounding.AwayFromZero);
				if (quer < AlcancePiso)
				{ porque = $"o alcance minimo e de {AlcancePiso} tiles."; return false; }
				int cobra = quer - (int)Alcance;
				if (!Cobrar(cobra, out porque)) return false;
				Alcance = quer;
				return true;
			}

			// ---------------------------------------------------------- modificador de distancia (`:1370`)
			case Compra.DistanciaMod:
			{
				if (!AceitaModificadoresDeRaio)
				{ porque = "so o raio ganha ou perde forca com a distancia."; return false; }
				double quer = Math.Round(arg, 1, MidpointRounding.AwayFromZero);
				if (quer < DistModPiso)
				{ porque = $"o modificador minimo e {DistModPiso:0.0}."; return false; }
				// ============ O TEXTO DO ORIGINAL MENTE, E O CODIGO E QUE VALE ============
				// O alerta do DM diz *"Costs 2 points in either direction"*, mas a conta logo abaixo
				// e `round((amount - rangemodifier) * 10)`: DEZ pontos por 1,0 de modificador, ou
				// seja UM ponto a cada 0,1. Com 5 pontos da pra ir de 1,0 a no maximo 1,5.
				// Portado o CODIGO. Um texto e uma promessa; a conta e o jogo.
				// ==========================================================================
				int cobra = (int)Math.Round((quer - DistanciaMod) * 10, MidpointRounding.AwayFromZero);
				if (!Cobrar(cobra, out porque)) return false;
				DistanciaMod = quer;
				return true;
			}
		}

		porque = "essa compra nao existe.";
		return false;
	}

	/// <summary>
	/// A GUARDA UNICA, E O UNICO LUGAR QUE ESCREVE EM <see cref="Gasto"/>.
	///
	/// <paramref name="custo"/> positivo e compra (vantagem), negativo e estorno (desvantagem). As
	/// duas bordas sao conferidas AQUI e o saldo so anda se as duas passarem -- por isso os dezoito
	/// `case` do <see cref="Aplicar"/> nao tem nenhuma conta de ponto propria.
	///
	/// ============================ POR QUE NUM LUGAR SO ============================
	/// No DM a guarda do teto e um `if (spent + N <= total)` COPIADO dentro de cada um dos dezoito
	/// verbos -- e tres das dezoito copias sairam diferentes das outras quinze (a da estamina e a da
	/// carga conferem um numero e cobram outro; a do ramo fino da velocidade nao existe). Nenhuma
	/// delas foi escrita errada de proposito: elas foram escritas DE NOVO, dezoito vezes.
	///
	/// Repetir isso pro piso -- dezoito `if (Gasto - N >= 0)` -- seria pedir os mesmos tres erros de
	/// novo, agora com o dobro de linhas pra conferir. Aqui ha um `if` de teto e um `if` de piso,
	/// os dois na mesma tela, e o botao que alguem acrescentar amanha ja nasce coberto pelos dois.
	/// ==========================================================================
	///
	/// ============================ O PISO RECUSA, E NAO ENGOLE ============================
	/// Com <see cref="Gasto"/> em zero, uma desvantagem (dano pra baixo, carga mais longa, Ki mais
	/// caro, tiro mais lento, ligar folego) nao rende ponto nenhum -- nao ha o que devolver. Havia
	/// duas saidas defensaveis, e a escolhida foi RECUSAR o clique inteiro em vez de aplicar a
	/// desvantagem e simplesmente nao pagar por ela:
	///
	///   1. PERDER seria mais curto de escrever (um `Math.Max(0, ...)` e pronto), mas o jogador
	///      pagaria o preco REAL -- uma tecnica mais fraca, de verdade, pra sempre -- e receberia
	///      nada em troca, sem uma palavra. Isso e a armadilha 5 da casa em pessoa: silencio no
	///      lugar de erro. E o pior tipo dela, porque o estado MUDA -- nao da nem pra desconfiar de
	///      que o clique nao pegou.
	///   2. RECUSAR custa uma frase e devolve a decisao pro jogador: ele ve que nao ha o que
	///      estornar, gasta primeiro, estorna depois. Nada acontece pelas costas dele.
	///
	/// E ha uma razao de engenharia junto: RECUSANDO, <see cref="Gasto"/> continua sendo exatamente
	/// a soma dos precos de tudo que foi aplicado. Todo clique fica desfazivel pelo seu par, e o
	/// `DesfazerModificadoresDeRaio` do servidor -- que refaz uma tecnica passo a passo pelo funil --
	/// continua batendo. ENGOLINDO, o saldo passaria a depender da ORDEM dos cliques (descer o dano
	/// e subir de volta cobraria 1 ponto do nada), e a tecnica carregaria ajustes que ninguem pagou.
	/// ==================================================================================
	/// </summary>
	private bool Cobrar(int custo, out string porque)
	{
		int novo = Gasto + custo;

		if (novo > PontosTotais)
		{
			porque = custo == 1
				? $"nao ha ponto sobrando (voce tem {Restantes})."
				: $"isso custa {custo} pontos e voce tem {Restantes}.";
			return false;
		}

		if (novo < 0)
		{
			porque = Gasto == 0
				? $"nao ha o que devolver: os {PontosTotais} pontos ainda estao inteiros na sua mao."
				: $"isso devolveria {-custo} pontos e voce so gastou {Gasto}.";
			return false;
		}

		Gasto = novo;
		porque = "";
		return true;
	}

	/// <summary>
	/// Corta o lixo do ponto flutuante nos passos de 0,1 / 0,2 / 0,4.
	///
	/// Sem isto `0.8 - 0.1 - 0.1 - 0.1` chega em 0,5000000000000001 e o piso de 0,1 e testado
	/// contra um numero que nao e o que a tela mostra -- que e como um botao passa a recusar na
	/// setima vez sem motivo visivel. O DM nao precisa disto porque o BYOND ja guarda `num` como
	/// float de 32 bits e a soma vai pro mesmo lugar; aqui e `double`.
	/// </summary>
	private static double Arredonda(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
