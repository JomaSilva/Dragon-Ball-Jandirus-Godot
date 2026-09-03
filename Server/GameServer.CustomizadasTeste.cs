using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA CRIACAO DE TECNICAS (`--tecnicateste`) -- roda no BOOT, sem ninguem em jogo.
///
///     Godot --headless --path . --server --port 7961 --tecnicateste
///
/// ============================ O QUE ELA AFIRMA ============================
///  1. A TABELA DE PONTOS E A DO DM -- as dezoito compras, uma a uma, com o preco, o piso e o teto
///     de cada uma conferidos contra a linha do `customattacks.dm` de onde vieram.
///  2. OS DOIS TETOS DISPARAM. Nao "existem": disparam. As 10 tecnicas e os 5 pontos sao RECUSADOS
///     por chamada de producao, com motivo escrito.
///  2b. E O ORCAMENTO E UM INTERVALO FECHADO: `0 &lt;= Gasto &lt;= 5`. O piso em zero e decisao do
///     dono e nao porte (ver `TecnicaCustomizada.Gasto`) -- a desvantagem sem saldo e RECUSADA, e
///     nao aplicada de graca. O teto nao e afirmado por amostragem: a mesa e VARRIDA em largura,
///     todo estado alcancavel em ate seis cliques, nos tres tipos, e nenhuma sequencia chega a 6
///     pontos. Ver `VarrerTodasAsSequencias`.
///  3. A MESA EDITA UMA COPIA: descartar nao encosta na tecnica de pe (nem nos pontos dela).
///  4. ELAS ATRAVESSAM O DISCO, pelo `AccountStore` de verdade -- e save ANTIGO (campo nulo) entra
///     sem ramo de migracao.
///  5. AS TRES DISPARAM DE VERDADE, pelo mesmo `Disparar`/`Canalizar` das tecnicas portadas, com os
///     numeros que o jogador comprou chegando no projetil.
///  6. O VERBO `Custom_Attack&lt;n&gt;` chega, e um slot vazio e recusado DIZENDO por que.
/// =========================================================================
///
/// ============================ CHAMA O CODIGO DE PRODUCAO ============================
/// Nao ha uma segunda tabela de precos, nao ha um `ComprarDeTeste` e nao ha um disparo paralelo.
/// As compras entram por <c>TecnicaCustomizada.Aplicar</c> (o mesmo metodo que o verbo
/// `ca_comprar` chama), as tecnicas nascem por <c>CriarTecnica</c>/<c>SalvarTecnica</c>, e os tiros
/// saem por <c>UsarTecnicaCustomizada</c> -- o mesmo que o `C2S.Habilidade` de um jogador aciona.
/// ==================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _tcOk, _tcFalhou;

	private void AfirmarTc(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _tcOk++; GD.Print($"[tecnica]   OK    {oque}"); return; }
		_tcFalhou++;
		GD.PrintErr($"[tecnica]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDeTecnicas()
	{
		_tcOk = _tcFalhou = 0;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);   // a familia 5 precisa de chao livre
		GD.Print("[tecnica] ================ A CRIACAO DE TECNICAS DE KI ================");

		try
		{
			ATabelaDePontos();
			OsTetosDisparam();
			AMesaEditaUmaCopia();
			ElasAtravessamODisco();
			AsTresDisparam();
			ODespachoDoVerbo();
		}
		finally
		{
			EscutaDeAvisos = null;
			LimparTudoDaBancada();
		}

		GD.Print($"[tecnica] ================ {_tcOk} passaram, {_tcFalhou} falharam ================");
	}

	/// <summary>Uma tecnica nova, no padrao do DM, do tipo pedido.</summary>
	private static TecnicaCustomizada Nova(TipoDeProjetil tipo = TipoDeProjetil.Beam)
	{
		var t = new TecnicaCustomizada { Id = 1 };
		t.PorTipo(tipo);
		return t;
	}

	/// <summary>Aplica e devolve se passou. O `porque` fica no <paramref name="motivo"/>.</summary>
	private static bool Comprar(TecnicaCustomizada t, Compra c, double arg, out string motivo)
		=> t.Aplicar(c, arg, out motivo);

	private static bool Comprar(TecnicaCustomizada t, Compra c) => t.Aplicar(c, 0, out _);

	// =====================================================================
	// 1) A TABELA DE PONTOS, LINHA A LINHA CONTRA O DM
	// =====================================================================
	/// <summary>
	/// AS DEZOITO COMPRAS. Cada afirmacao cita a linha do `customattacks.dm` de onde o numero saiu.
	///
	/// ============================ POR QUE ELA E TAO LONGA ============================
	/// Porque no original ela E longa: dezoito `verb`, cada um com a sua propria copia da guarda
	/// `if (spent + N <= total)`. Testar "algumas" seria testar as que eu ja sabia que estavam
	/// certas -- e as tres que divergiam do resto (estamina, carga e o ramo fino da velocidade) sao
	/// exatamente as que nao chamariam atencao.
	/// ============================================================================
	/// </summary>
	private void ATabelaDePontos()
	{
		GD.Print("[tecnica] -- 1) A TABELA DE PONTOS E A DO `customattacks.dm`");

		// ---------------------------------------------------------------- os padroes (`:77-92`)
		TecnicaCustomizada p = Nova();
		AfirmarTc("os padroes sao os do DM (dano 0,8 / carga 1 / Ki 20 / velocidade 1 / alcance 20)",
				  Math.Abs(p.BaseDano - 0.8) < 1e-9 && Math.Abs(p.CargaMinima - 1) < 1e-9
				  && Math.Abs(p.CustoKi - 20) < 1e-9 && Math.Abs(p.Velocidade - 1) < 1e-9
				  && Math.Abs(p.Alcance - 20) < 1e-9 && Math.Abs(p.DistanciaMod - 1) < 1e-9,
				  $"{p.BaseDano}/{p.CargaMinima}/{p.CustoKi}/{p.Velocidade}/{p.Alcance}");
		AfirmarTc("o orcamento e 5 (`total_custompoints`, `:87`) e comeca inteiro",
				  TecnicaCustomizada.PontosTotais == 5 && p.Gasto == 0 && p.Restantes == 5);

		// ---------------------------------------------------------------- dano: +-0,1 por ponto
		TecnicaCustomizada t = Nova();
		Comprar(t, Compra.DanoMais);
		AfirmarTc("dano +0,1 custa 1 ponto (`:962`)",
				  Math.Abs(t.BaseDano - 0.9) < 1e-9 && t.Gasto == 1, $"{t.BaseDano} / gasto {t.Gasto}");

		// ============================ TODO ESTORNO COMECA COM SALDO NA MAO ============================
		// Daqui pra baixo, toda medida de compra que RENDE ponto abre gastando primeiro. Nao e enfeite
		// de teste: com o piso em zero do dono (`TecnicaCustomizada.Gasto`), medir um estorno num
		// rascunho zerado mediria a RECUSA e nao o preco -- e a linha ficaria verde por acidente se
		// alguem trocasse o preco depois.
		// =========================================================================================
		t = Nova();
		Comprar(t, Compra.DanoMais);        // gasto 1, dano 0,9
		Comprar(t, Compra.DanoMenos);
		AfirmarTc("dano -0,1 DEVOLVE 1 ponto (`:973`)",
				  Math.Abs(t.BaseDano - 0.8) < 1e-9 && t.Gasto == 0, $"{t.BaseDano} / gasto {t.Gasto}");

		// O PISO DO CAMPO E O PISO DO SALDO SAO DOIS PISOS DIFERENTES, e este bloco separa os dois.
		// Com os 5 pontos gastos em outra coisa, cabem CINCO estornos de potencia (o saldo acaba antes
		// do campo); com o dano rebaixado ate 0,1, e o campo que acaba antes.
		t = Nova();
		// CINCO PONTOS LONGE DA POTENCIA: 4 degraus de velocidade (1 -> 5, o teto) e 1 de carga curta.
		for (int i = 0; i < 4; i++) Comprar(t, Compra.VelocidadeMais);
		Comprar(t, Compra.CargaMenos);
		AfirmarTc("cinco pontos gastos fora da potencia (4 de velocidade + 1 de carga)",
				  t.Gasto == 5, $"gasto {t.Gasto} / velocidade {t.Velocidade} / carga {t.CargaMinima}");
		int estornos = 0;
		while (Comprar(t, Compra.DanoMenos)) estornos++;
		AfirmarTc("o SALDO acaba antes do campo: 5 gastos = 5 estornos de potencia, e para no zero",
				  estornos == 5 && t.Gasto == 0 && Math.Abs(t.BaseDano - 0.3) < 1e-9,
				  $"{estornos} estornos / dano {t.BaseDano} / gasto {t.Gasto}");
		AfirmarTc("...e o sexto recusa DIZENDO por que (o clique nao enfraquece a tecnica de graca)",
				  !Comprar(t, Compra.DanoMenos, 0, out string pq) && pq.Length > 0
				  && Math.Abs(t.BaseDano - 0.3) < 1e-9, pq);

		// AGORA O PISO DO CAMPO. 7 degraus de 0,8 ate 0,1, e o saldo tem que dar conta dos 7 -- so que
		// o orcamento so vai a 5, entao ninguem chega no piso de 0,1 sem antes acabar o dinheiro. E
		// exatamente o que o dono cortou: no DM esse caminho rendia os 7 e abria 12 pontos.
		t = Nova();
		for (int i = 0; i < 4; i++) Comprar(t, Compra.VelocidadeMais);
		Comprar(t, Compra.CargaMenos);
		while (Comprar(t, Compra.DanoMenos)) { }
		AfirmarTc("o piso de dano de 0,1 (`:972`) ficou INALCANCAVEL: o orcamento de 5 acaba em 0,3",
				  Math.Abs(t.BaseDano - 0.3) < 1e-9 && t.Gasto == 0, $"{t.BaseDano} / gasto {t.Gasto}");
		AfirmarTc("...e o orcamento voltou inteiro, e nao engordado (o piso do dono)",
				  t.Restantes == TecnicaCustomizada.PontosTotais, $"restam {t.Restantes}");

		// ---------------------------------------------------------------- carga: +-0,4 s, piso 0,2
		t = Nova();
		Comprar(t, Compra.DanoMais);        // gasto 1
		Comprar(t, Compra.CargaMais);
		AfirmarTc("carga +0,4 s DEVOLVE 1 ponto (`:940`)",
				  Math.Abs(t.CargaMinima - 1.4) < 1e-9 && t.Gasto == 0, $"{t.CargaMinima}");

		t = Nova();
		Comprar(t, Compra.CargaMenos);
		Comprar(t, Compra.CargaMenos);
		AfirmarTc("carga -0,4 s custa 1 ponto, e de 1 s so cabem DOIS passos (`:948`)",
				  Math.Abs(t.CargaMinima - 0.2) < 1e-9 && t.Gasto == 2, $"{t.CargaMinima}");
		AfirmarTc("...o piso de carga e 0,2 s (`:947`)", !Comprar(t, Compra.CargaMenos, 0, out pq), pq);

		// ---------------------------------------------------------------- Ki: +-40, piso 20
		t = Nova();
		Comprar(t, Compra.DanoMais);        // gasto 1
		Comprar(t, Compra.KiMais);
		AfirmarTc("Ki +40 DEVOLVE 1 ponto (`:983`)",
				  Math.Abs(t.CustoKi - 60) < 1e-9 && t.Gasto == 0, $"{t.CustoKi}");
		Comprar(t, Compra.KiMenos);
		AfirmarTc("Ki -40 custa 1 ponto e volta ao padrao (`:991`)",
				  Math.Abs(t.CustoKi - 20) < 1e-9 && t.Gasto == 1, $"{t.CustoKi}");
		AfirmarTc("...e no padrao de 20 nao da pra baratear: o piso E o padrao (`:990`)",
				  !Comprar(t, Compra.KiMenos, 0, out pq), pq);

		// ---------------------------------------------------------------- estamina: ELA DEVOLVE 2
		// E OS 2 PRECISAM EXISTIR. Este e o unico estorno de DOIS pontos da tabela, entao e o unico
		// que um unico ponto gasto nao paga -- e por isso ele abre com quatro.
		t = Nova();
		for (int i = 0; i < 4; i++) Comprar(t, Compra.DanoMais);   // gasto 4

		TecnicaCustomizada pobre = Nova();
		Comprar(pobre, Compra.DanoMais);   // gasto 1 -- um a menos do que o folego devolve
		AfirmarTc("com 1 ponto gasto, ligar o folego (que devolve 2) e RECUSADO com motivo",
				  !Comprar(pobre, Compra.StaminaLigar, 0, out pq) && pq.Length > 0
				  && !pobre.UsaStamina, pq);

		Comprar(t, Compra.StaminaLigar);
		AfirmarTc("ligar o folego DEVOLVE 2 pontos e crava o custo em 1 (`:1008`)",
				  t.UsaStamina && Math.Abs(t.CustoStamina - 1) < 1e-9 && t.Gasto == 2,
				  $"gasto {t.Gasto} / custo {t.CustoStamina}");
		Comprar(t, Compra.StaminaMais);
		Comprar(t, Compra.StaminaMais);
		AfirmarTc("cada ponto de folego a mais DEVOLVE 1 (`:1025`)",
				  Math.Abs(t.CustoStamina - 3) < 1e-9 && t.Gasto == 0, $"{t.CustoStamina} / {t.Gasto}");
		Comprar(t, Compra.StaminaDesligar);
		AfirmarTc("desligar cobra o folego de volta MAIS os 2 (`:1013`): 0 + 4 = 4",
				  !t.UsaStamina && t.CustoStamina == 0 && t.Gasto == 4, $"gasto {t.Gasto}");

		t = Nova();
		for (int i = 0; i < 2; i++) Comprar(t, Compra.DanoMais);
		Comprar(t, Compra.StaminaLigar);
		AfirmarTc("o piso do folego e 1 (`:1031`)", !Comprar(t, Compra.StaminaMenos, 0, out pq), pq);

		// ---------------------------------------------------------------- velocidade: dois passos
		t = Nova();
		for (int i = 0; i < 4; i++) Comprar(t, Compra.VelocidadeMais);
		AfirmarTc("velocidade sobe de 1 em 1 ate 5, 1 ponto cada (`:1047`)",
				  Math.Abs(t.Velocidade - 5) < 1e-9 && t.Gasto == 4, $"{t.Velocidade} / {t.Gasto}");
		AfirmarTc("...e 5 e o teto (`:1046`)", !Comprar(t, Compra.VelocidadeMais, 0, out pq), pq);

		t = Nova();
		for (int i = 0; i < 4; i++) Comprar(t, Compra.DanoMais);   // gasto 4, pra financiar os 4 degraus
		for (int i = 0; i < 4; i++) Comprar(t, Compra.VelocidadeMenos);
		AfirmarTc("abaixo de 1 o passo e 0,2 e cada degrau DEVOLVE 1 (`:1065`)",
				  Math.Abs(t.Velocidade - 0.2) < 1e-9 && t.Gasto == 0, $"{t.Velocidade} / {t.Gasto}");
		AfirmarTc("...e o piso e 0,2 (`:1064`), que aqui chega antes do piso do saldo",
				  !Comprar(t, Compra.VelocidadeMenos, 0, out pq), pq);

		// ---------------------------------------------------------------- instantaneo: 2 pontos
		t = Nova();
		Comprar(t, Compra.InstantaneoLigar);
		AfirmarTc("ataque instantaneo custa 2 pontos (`:1321`)",
				  t.Instantaneo && t.Gasto == 2, $"gasto {t.Gasto}");
		Comprar(t, Compra.InstantaneoDesligar);
		AfirmarTc("...e tira-lo devolve os mesmos 2 (`:1334`)", !t.Instantaneo && t.Gasto == 0);

		AfirmarTc("instantaneo e MODIFICADOR DE RAIO: a bola recusa (`:1306`)",
				  !Comprar(Nova(TipoDeProjetil.Blast), Compra.InstantaneoLigar, 0, out pq), pq);

		// ---------------------------------------------------------------- alcance: 1 ponto por tile
		t = Nova();
		Comprar(t, Compra.Alcance, 25, out _);
		AfirmarTc("alcance +5 tiles custa 5 pontos, 1 por tile (`:1346`)",
				  Math.Abs(t.Alcance - 25) < 1e-9 && t.Gasto == 5, $"{t.Alcance} / {t.Gasto}");

		// -5 TILES DEVOLVE 5, E OS 5 PRECISAM TER SIDO GASTOS. E o maior estorno de um clique so da
		// tabela, entao e ele que mostra o piso agindo sobre um numero grande e nao sobre o 1.
		t = Nova();
		AfirmarTc("encurtar o alcance sem nada gasto e RECUSADO (o estorno de 5 nao tem de onde sair)",
				  !Comprar(t, Compra.Alcance, 15, out pq)
				  && Math.Abs(t.Alcance - TecnicaCustomizada.AlcancePadrao) < 1e-9, pq);
		for (int i = 0; i < 5; i++) Comprar(t, Compra.DanoMais);
		Comprar(t, Compra.Alcance, 15, out _);
		AfirmarTc("...e com os 5 gastos ele DEVOLVE os 5", Math.Abs(t.Alcance - 15) < 1e-9 && t.Gasto == 0,
				  $"{t.Alcance} / {t.Gasto}");
		AfirmarTc("o alcance minimo e 5 tiles (`:1347`)",
				  !Comprar(Nova(), Compra.Alcance, 4, out pq), pq);
		AfirmarTc("alcance tambem e so de raio", !Comprar(Nova(TipoDeProjetil.Guided), Compra.Alcance, 25, out pq), pq);

		// ---------------------------------------------------------------- modificador de distancia
		t = Nova();
		Comprar(t, Compra.DistanciaMod, 1.5, out _);
		AfirmarTc("o modificador de distancia custa 1 ponto a cada 0,1 -- `round((x-rm)*10)` (`:1377`)",
				  Math.Abs(t.DistanciaMod - 1.5) < 1e-9 && t.Gasto == 5, $"{t.DistanciaMod} / {t.Gasto}");
		AfirmarTc("...e por isso 1,6 NAO cabe nos 5 pontos (o texto do DM que dizia \"2 points\" mentia)",
				  !Comprar(Nova(), Compra.DistanciaMod, 1.6, out pq), pq);
		AfirmarTc("o minimo do modificador e 0,5 (`:1378`)",
				  !Comprar(Nova(), Compra.DistanciaMod, 0.4, out pq), pq);

		// ---------------------------------------------------------------- carregavel
		t = Nova();
		Comprar(t, Compra.DanoMais);
		Comprar(t, Compra.DanoMais);    // gasto 2, pra pagar os dois degraus de carga
		Comprar(t, Compra.CargaMais);
		Comprar(t, Compra.CargaMais);   // carga 1,8 s, gasto 0
		Comprar(t, Compra.CarregavelDesligar);
		AfirmarTc("desligar a carga cobra `(carga-1)*2,5 + 1` (`:922`, `:929`): 2 + 1 = 3",
				  !t.Carregavel && Math.Abs(t.CargaMinima - 1) < 1e-9 && t.Gasto == 3,
				  $"carga {t.CargaMinima} / gasto {t.Gasto}");
		AfirmarTc("...e o 2,5 magico do DM E o inverso do passo de 0,4 s",
				  Math.Abs(TecnicaCustomizada.PontosPorSegundoDeCarga - 2.5) < 1e-9);

		// ---------------------------------------------------------------- o tipo manda na carga
		AfirmarTc("o raio nasce carregando e a bola nao (`PickAttackType`, `:874`/`:885`)",
				  Nova().Carregavel && !Nova(TipoDeProjetil.Blast).Carregavel
				  && !Nova(TipoDeProjetil.Guided).Carregavel);
	}

	// =====================================================================
	// 2) OS TETOS
	// =====================================================================
	/// <summary>
	/// OS DOIS TETOS, E ELES DISPARAM.
	///
	/// A regra 0.7 da casa em pessoa: *"um teto que nunca e atingido e indistinguivel de teto
	/// nenhum"*. Aqui os dois sao levados ate a borda por chamada de producao, e a recusa e LIDA --
	/// nao basta o estado nao mudar, o jogador tem que ouvir por que.
	/// </summary>
	private void OsTetosDisparam()
	{
		GD.Print("[tecnica] -- 2) OS DOIS TETOS DISPARAM (10 tecnicas, 5 pontos)");

		// ---------------------------------------------------------------- o teto de pontos
		TecnicaCustomizada t = Nova();
		for (int i = 0; i < 5; i++) Comprar(t, Compra.DanoMais);
		AfirmarTc("os 5 pontos gastam-se inteiros", t.Gasto == 5 && t.Restantes == 0);
		AfirmarTc("...e o SEXTO e recusado com motivo",
				  !Comprar(t, Compra.DanoMais, 0, out string pq) && pq.Length > 0, pq);
		AfirmarTc("...e a recusa nao mexeu em nada", Math.Abs(t.BaseDano - 1.3) < 1e-9 && t.Gasto == 5,
				  $"{t.BaseDano} / {t.Gasto}");

		// UMA COMPRA DE 2 COM 1 PONTO NA MAO: a guarda tem que olhar o CUSTO e nao "sobrou alguma
		// coisa?". Foi este `if` que o DM escreveu diferente em tres lugares.
		t = Nova();
		for (int i = 0; i < 4; i++) Comprar(t, Compra.DanoMais);
		AfirmarTc("com 1 ponto na mao, uma compra de 2 e recusada (o instantaneo)",
				  t.Restantes == 1 && !Comprar(t, Compra.InstantaneoLigar, 0, out pq), pq);

		// ---------------------------------------------------------------- o piso, pelo outro lado
		// O PISO EM ZERO DO DONO, NA BORDA. Uma desvantagem com o orcamento inteiro na mao nao rende
		// nada -- e a decisao foi RECUSAR o clique, e nao aplica-lo de graca. Entao o campo tem que
		// ficar PARADO junto com o saldo: se a potencia caisse pra 0,7 e o gasto ficasse em 0, o
		// jogador teria pago a desvantagem e recebido nada.
		t = Nova();
		AfirmarTc("com 0 gasto, a desvantagem e recusada COM MOTIVO e a tecnica nao muda",
				  !Comprar(t, Compra.DanoMenos, 0, out pq) && pq.Length > 0
				  && Math.Abs(t.BaseDano - TecnicaCustomizada.DanoPadrao) < 1e-9 && t.Gasto == 0,
				  $"{pq} | dano {t.BaseDano} gasto {t.Gasto}");

		// ---------------------------------------------------------------- a invariante, VARRIDA
		VarrerTodasAsSequencias();

		// ---------------------------------------------------------------- o teto de dez tecnicas
		ServerPlayer pl = Forjar("Inventor", CorredorLivre(4), bp: 5_000);
		EscutaDeAvisos = [];
		for (int i = 0; i < TecnicaCustomizada.Maximo; i++)
		{
			CriarTecnica(pl);
			if (pl.Mesa != null) pl.Mesa.Nome = $"Tecnica {i + 1}";
			SalvarTecnica(pl);
		}
		AfirmarTc($"{TecnicaCustomizada.Maximo} tecnicas cabem, com ids 1..{TecnicaCustomizada.Maximo}",
				  pl.Customizadas.Count == TecnicaCustomizada.Maximo
				  && pl.Customizadas[0].Id == 1
				  && pl.Customizadas[^1].Id == TecnicaCustomizada.Maximo,
				  $"{pl.Customizadas.Count}");

		EscutaDeAvisos.Clear();
		CriarTecnica(pl);
		AfirmarTc("...a DECIMA PRIMEIRA e recusada, e a mesa nem abre",
				  pl.Mesa == null && pl.Customizadas.Count == TecnicaCustomizada.Maximo);
		AfirmarTc("...e o jogador OUVE por que (o `switch` sem `else` do DM saia calado)",
				  EscutaDeAvisos.Exists(a => a.Contains("cabem", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", EscutaDeAvisos));

		// ESQUECER ABRE O SLAO CERTO. `Count+1` reusaria um id ocupado -- ver `CriarTecnica`.
		EsquecerTecnica(pl, "4");
		CriarTecnica(pl);
		AfirmarTc("esquecer a 4 libera o ID 4, e nao o 11",
				  pl.Mesa?.Id == 4, $"id {pl.Mesa?.Id}");
		CancelarMesa(pl);

		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	/// <summary>
	/// ============================ NAO EXISTE SEQUENCIA DE CLIQUES QUE GASTE MAIS DE 5 ============================
	/// Esta e a afirmacao que faltava, e ela e a razao inteira do orcamento existir. As de cima
	/// provam que ESTE clique e recusado; esta prova que NENHUMA combinacao de cliques chega la --
	/// que e coisa diferente, porque o furo de orcamento nunca mora num botao, mora no caminho.
	/// Foi assim no proprio DM: o ramo fino da velocidade (divergencia 2 de 3) so estourava o teto
	/// pra quem tivesse DESCIDO a velocidade abaixo de 1 antes. Um clique isolado nunca acharia.
	///
	/// E VARREDURA, E NAO AMOSTRA. O que estava aqui antes eram 4000 cliques a esmo com semente
	/// fixa: aquilo passa por muita coisa, mas so responde "nao achei", e uma semente diferente
	/// responderia outra coisa. Aqui a mesa e explorada em LARGURA -- todo estado alcancavel, todo
	/// botao a partir dele -- com um conjunto de estados ja vistos pra nao repetir caminho. Quando a
	/// fila esvazia, a resposta e "nao existe", e nao "nao achei".
	///
	/// O TETO DE PROFUNDIDADE E DE ESTADOS existe porque a mesa e INFINITA: `DanoMais` e `CargaMais`
	/// nao tem teto de campo nenhum, entao da pra gastar 5 na potencia, recuperar 5 alongando a
	/// carga, gastar 5 na potencia de novo, pra sempre -- cada volta num estado novo. O corte e
	/// honesto e esta escrito: varre-se tudo ate `Passos` cliques.
	///
	/// COMO ELA REPROVA: tire o `if (novo > PontosTotais)` do `Cobrar` e ela acha o caminho em
	/// milissegundos e IMPRIME a sequencia de botoes que estourou -- que e o que se quer ler as
	/// 3h da manha, e nao "a invariante falhou".
	/// ======================================================================================================
	/// </summary>
	private void VarrerTodasAsSequencias()
	{
		const int Passos = 6;
		const int TetoDeEstados = 250_000;

		// OS ARGUMENTOS DOS DOIS BOTOES QUE PEDEM NUMERO. Escolhidos nas BORDAS e nao a esmo: os dois
		// pisos, os dois padroes, um passo pra cada lado deles, e um valor bem longe -- que e onde um
		// preco calculado (`quer - Alcance`, `(quer - mod) * 10`) erra, se for errar.
		double[] alcances = [4, 5, 6, 19, 20, 21, 25, 60];
		double[] mods = [0.4, 0.5, 0.6, 0.9, 1.0, 1.1, 1.5, 2.0];

		// Cada "clique" e um botao mais o argumento dele. E esta lista que representa o painel.
		List<(Compra C, double Arg)> cliques = [];
		foreach (Compra c in Enum.GetValues<Compra>())
		{
			if (c == Compra.Alcance) foreach (double a in alcances) cliques.Add((c, a));
			else if (c == Compra.DistanciaMod) foreach (double a in mods) cliques.Add((c, a));
			else cliques.Add((c, 0));
		}

		// A CHAVE DO ESTADO. Tem que ser TUDO que uma compra pode mexer -- um campo de fora deixaria
		// dois estados diferentes parecerem iguais e a varredura pularia o caminho que importa.
		static string Chave(TecnicaCustomizada t) =>
			$"{t.Tipo}|{t.BaseDano:0.####}|{t.CargaMinima:0.####}|{t.CustoKi:0.####}|{t.CustoStamina:0.####}"
			+ $"|{(t.UsaStamina ? 1 : 0)}{(t.Carregavel ? 1 : 0)}{(t.Instantaneo ? 1 : 0)}"
			+ $"|{t.Velocidade:0.####}|{t.Alcance:0.####}|{t.DistanciaMod:0.####}|{t.Gasto}";

		int estourou = 0, afundou = 0, vistos = 0, maiorGasto = 0, cortouPorTeto = 0;
		bool viuOTeto = false, viuORecusar = false;
		string caminhoRuim = "";

		// OS TRES TIPOS SAO TRES PAINEIS DIFERENTES: os modificadores de raio so existem no Beam, e o
		// `Carregavel` nasce diferente em cada um. Varrer so o raio deixaria a bola sem varredura.
		foreach (TipoDeProjetil tipo in Enum.GetValues<TipoDeProjetil>())
		{
			if (tipo is not (TipoDeProjetil.Beam or TipoDeProjetil.Blast or TipoDeProjetil.Guided)) continue;

			var jaVi = new HashSet<string>();
			var fila = new Queue<(TecnicaCustomizada T, int Passo, string Caminho)>();
			TecnicaCustomizada raiz = Nova(tipo);
			jaVi.Add(Chave(raiz));
			fila.Enqueue((raiz, 0, ""));

			while (fila.Count > 0)
			{
				(TecnicaCustomizada atual, int passo, string caminho) = fila.Dequeue();
				if (passo >= Passos) continue;

				foreach ((Compra c, double arg) in cliques)
				{
					TecnicaCustomizada filho = atual.Clonar();
					bool passou = filho.Aplicar(c, arg, out _);
					if (!passou) { viuORecusar = true; continue; }

					string trilha = caminho.Length == 0 ? $"{c}({arg:0.#})" : $"{caminho} -> {c}({arg:0.#})";

					// AS DUAS BORDAS, MEDIDAS NO ESTADO E NAO NA CHAMADA. Guardar a trilha inteira e o
					// que transforma "falhou" em "clique isto, nesta ordem".
					if (filho.Gasto > TecnicaCustomizada.PontosTotais)
					{ estourou++; if (caminhoRuim.Length == 0) caminhoRuim = trilha; }
					if (filho.Gasto < 0)
					{ afundou++; if (caminhoRuim.Length == 0) caminhoRuim = trilha; }

					if (filho.Gasto > maiorGasto) maiorGasto = filho.Gasto;
					if (filho.Gasto == TecnicaCustomizada.PontosTotais) viuOTeto = true;

					if (!jaVi.Add(Chave(filho))) continue;
					vistos++;
					if (jaVi.Count >= TetoDeEstados) { cortouPorTeto++; fila.Clear(); break; }
					fila.Enqueue((filho, passo + 1, trilha));
				}
			}
		}

		AfirmarTc($"VARREDURA: {vistos} mesas alcancaveis em ate {Passos} cliques, e NENHUMA passa dos "
				  + $"{TecnicaCustomizada.PontosTotais} pontos",
				  estourou == 0, $"{estourou} estados furaram o teto, o primeiro por: {caminhoRuim}");
		AfirmarTc("...e nenhuma delas afunda abaixo do piso de zero (o corte do dono, pelo mesmo funil)",
				  afundou == 0, $"{afundou} estados furaram o piso, o primeiro por: {caminhoRuim}");

		// ---- A VARREDURA NAO PODE SER VAZIA ----
		// Uma busca que nao anda tambem devolve "nenhum furo". Estas tres linhas sao o que separa
		// "varri e nao existe" de "nao varri". A regra 0.7 de novo: um teto que nunca e atingido e
		// indistinguivel de teto nenhum -- entao a varredura tem que PROVAR que encostou nele.
		AfirmarTc("...e a varredura andou de verdade (milhares de mesas, e nao um punhado)",
				  vistos > 5_000 && cortouPorTeto == 0,
				  $"{vistos} mesas, cortou por teto de estados {cortouPorTeto} vez(es)");
		AfirmarTc("...e ela ENCOSTOU no teto: houve mesa com os 5 pontos gastos",
				  viuOTeto && maiorGasto == TecnicaCustomizada.PontosTotais, $"maior gasto visto {maiorGasto}");
		AfirmarTc("...e houve recusa pelo caminho (se nada fosse recusado, nao haveria guarda testada)",
				  viuORecusar);
	}

	// =====================================================================
	// 3) A MESA EDITA UMA COPIA
	// =====================================================================
	private void AMesaEditaUmaCopia()
	{
		GD.Print("[tecnica] -- 3) A MESA EDITA UMA COPIA (descartar nao encosta na tecnica de pe)");

		ServerPlayer pl = Forjar("Copista", CorredorLivre(4), bp: 5_000);
		CriarTecnica(pl);
		pl.Mesa!.Nome = "Original";
		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		SalvarTecnica(pl);

		TecnicaCustomizada firme = pl.Customizadas[0];
		double danoAntes = firme.BaseDano;
		int gastoAntes = firme.Gasto;

		EditarTecnica(pl, "1");
		AfirmarTc("abrir pra ajustar cria uma COPIA, e nao a propria",
				  pl.Mesa != null && !ReferenceEquals(pl.Mesa, firme));

		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		TextoDaMesa(pl, "nome/Estragada");
		AfirmarTc("...mexer na copia NAO mexe na tecnica de pe",
				  Math.Abs(firme.BaseDano - danoAntes) < 1e-9 && firme.Gasto == gastoAntes
				  && firme.Nome == "Original",
				  $"{firme.Nome} {firme.BaseDano} / {firme.Gasto}");

		CancelarMesa(pl);
		AfirmarTc("...e descartar joga a copia fora inteira (nome, numeros e PONTOS)",
				  pl.Mesa == null && pl.Customizadas[0].Nome == "Original"
				  && Math.Abs(pl.Customizadas[0].BaseDano - danoAntes) < 1e-9
				  && pl.Customizadas[0].Gasto == gastoAntes);

		// CONFIRMAR, AI SIM, ESCREVE.
		EditarTecnica(pl, "1");
		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		TextoDaMesa(pl, "nome/Ajustada");
		SalvarTecnica(pl);
		AfirmarTc("confirmar grava a copia por cima (`CreateCustomSkill`, `:1211`)",
				  pl.Customizadas[0].Nome == "Ajustada"
				  && Math.Abs(pl.Customizadas[0].BaseDano - (danoAntes + 0.1)) < 1e-9
				  && pl.Customizadas.Count == 1,
				  $"{pl.Customizadas[0].Nome} {pl.Customizadas[0].BaseDano}");

		// O TIPO NAO MUDA DEPOIS DE PRONTA (`:1205`).
		EditarTecnica(pl, "1");
		EscutaDeAvisos = [];
		TipoDaTecnica(pl, "blast");
		AfirmarTc("o tipo de uma tecnica pronta nao muda, e a recusa e dita",
				  pl.Mesa!.Tipo == TipoDeProjetil.Beam && EscutaDeAvisos.Count > 0,
				  string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;
		CancelarMesa(pl);

		TrocarDeTipoNaoDaPontoDeGraca(pl);
		LimparTudoDaBancada();
	}

	/// <summary>
	/// ============================ TROCAR DE TIPO NAO IMPRIME PONTO ============================
	/// Este e um buraco que ESTA IMPLEMENTACAO teve, e nao o DM: os modificadores de raio so existem
	/// no Beam, entao sair do Beam tem que desfaze-los -- e a primeira versao desfazia com o
	/// resultado IGNORADO (`Aplicar(..., out _)`). Rebaixar o alcance rende pontos; gastar esses
	/// pontos em outra coisa e so entao trocar de tipo fazia o estorno nao caber, falhar calado, e a
	/// tecnica ficava com pontos que ninguem pagou.
	///
	/// A afirmacao vale pelos dois lados: o caminho que CABE tem que passar (e devolver os pontos do
	/// alcance comprado), e o que NAO cabe tem que ser recusado com motivo, sem mexer em nada.
	/// =====================================================================================
	/// </summary>
	private void TrocarDeTipoNaoDaPontoDeGraca(ServerPlayer pl)
	{
		// (a) O CAMINHO QUE CABE: alcance comprado volta pro padrao e devolve os pontos.
		CriarTecnica(pl);
		ComprarNaMesa(pl, $"{nameof(Compra.Alcance)}/25");
		ComprarNaMesa(pl, $"{nameof(Compra.CargaMenos)}");
		AfirmarTc("(raio) 25 tiles + carga curta = 6 pontos... que nao cabem",
				  pl.Mesa!.Gasto == 5 && Math.Abs(pl.Mesa.Alcance - 25) < 1e-9
				  && Math.Abs(pl.Mesa.CargaMinima - 1) < 1e-9,
				  $"gasto {pl.Mesa.Gasto} alcance {pl.Mesa.Alcance} carga {pl.Mesa.CargaMinima}");

		TipoDaTecnica(pl, "blast");
		AfirmarTc("virar bola desfaz o alcance e DEVOLVE os 5 pontos",
				  pl.Mesa!.Tipo == TipoDeProjetil.Blast && pl.Mesa.Gasto == 0
				  && Math.Abs(pl.Mesa.Alcance - TecnicaCustomizada.AlcancePadrao) < 1e-9
				  && !pl.Mesa.Carregavel,
				  $"gasto {pl.Mesa.Gasto} alcance {pl.Mesa.Alcance}");
		CancelarMesa(pl);

		// (b) O CAMINHO QUE NAO CABE: o alcance de 25 custou os 5 pontos, e depois um deles voltou pro
		// bolso alongando a carga -- entao desfazer o alcance quer devolver 5 num saldo de 4, e isso
		// afundaria o gasto abaixo do piso de zero.
		//
		// COM O SALDO NEGATIVO ISTO ERA OUTRO CENARIO (rebaixar o alcance rendia 5 e o estorno e que
		// nao cabia no TETO). O buraco que a afirmacao guarda e o mesmo dos dois jeitos: um estorno
		// pode falhar, e um estorno que falha calado deixa a tecnica com ajuste que ninguem pagou.
		CriarTecnica(pl);
		ComprarNaMesa(pl, $"{nameof(Compra.Alcance)}/25");   // -5  => gasto 5
		ComprarNaMesa(pl, nameof(Compra.CargaMais));         // +1  => gasto 4
		int gastoAntes = pl.Mesa!.Gasto;
		double alcanceAntes = pl.Mesa.Alcance;
		AfirmarTc("(raio) 25 tiles pagos e um ponto de volta na carga deixam o saldo em 4",
				  gastoAntes == 4 && Math.Abs(alcanceAntes - 25) < 1e-9
				  && Math.Abs(pl.Mesa.CargaMinima - 1.4) < 1e-9,
				  $"gasto {gastoAntes} alcance {alcanceAntes} carga {pl.Mesa.CargaMinima}");

		EscutaDeAvisos = [];
		TipoDaTecnica(pl, "blast");
		AfirmarTc("trocar de tipo com o estorno IMPAGAVEL e RECUSADO, e nao imprime ponto",
				  pl.Mesa!.Tipo == TipoDeProjetil.Beam && pl.Mesa.Gasto == gastoAntes
				  && Math.Abs(pl.Mesa.Alcance - alcanceAntes) < 1e-9
				  && EscutaDeAvisos.Count > 0,
				  $"tipo {pl.Mesa.Tipo} gasto {pl.Mesa.Gasto} (era {gastoAntes}) | "
				  + string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;
		CancelarMesa(pl);

		// (c) A CAIXINHA DE CARGA NAO E COBRADA NA TROCA. Ela nunca foi PAGA num raio
		// (`PickAttackType` a crava de graca, `:874`), entao desfazer o TEMPO nao pode cobrar o "+1"
		// do `ChargeAttackCheckmark`.
		CriarTecnica(pl);
		ComprarNaMesa(pl, nameof(Compra.DanoMais));    // gasto 1, pra financiar o degrau de carga
		ComprarNaMesa(pl, nameof(Compra.CargaMais));   // carga 1,4 -- devolveu o ponto, gasto 0
		TipoDaTecnica(pl, "guided");
		// O NUMERO E QUE PROVA: voltar UM degrau de carga cobra 1 (gasto 0 -> 1). Pela caixinha
		// (`CarregavelDesligar`) seriam 2 -- `(1,4-1)*2,5 = 1` mais o "+1" da caixinha --, e um gasto 2
		// aqui seria exatamente o sintoma de estar cobrando por um estorno que ninguem recebeu.
		AfirmarTc("a carga volta ao padrao pelos DEGRAUS (1 ponto), e nao pela caixinha (2)",
				  pl.Mesa!.Tipo == TipoDeProjetil.Guided && pl.Mesa.Gasto == 1
				  && Math.Abs(pl.Mesa.CargaMinima - TecnicaCustomizada.CargaPadrao) < 1e-9,
				  $"gasto {pl.Mesa.Gasto} carga {pl.Mesa.CargaMinima}");
		CancelarMesa(pl);
	}

	// =====================================================================
	// 4) O DISCO
	// =====================================================================
	/// <summary>
	/// ============================ ELAS ATRAVESSAM O DISCO? ============================
	/// Esta e a checagem que uma bancada de `Core` NAO faz, e ela ja pegaria um defeito real deste
	/// projeto duas vezes: as cores de roupa foram escritas e usadas por meses sem NUNCA persistir
	/// (campo `readonly` que o `System.Text.Json` ignorava calado), e os `Limiares` sumiam do save
	/// porque a linha de escrita nunca foi acrescentada ao `DeJogador`.
	///
	/// Uma tecnica e meia hora de fucar num painel. O sintoma de ela nao persistir seria "as vezes
	/// minhas tecnicas somem", que ninguem liga a um serializador.
	/// ============================================================================
	/// </summary>
	private void ElasAtravessamODisco()
	{
		GD.Print("[tecnica] -- 4) ELAS ATRAVESSAM O DISCO (pelo `AccountStore` de verdade)");

		ServerPlayer pl = Forjar("Arquivista", CorredorLivre(4), bp: 5_000);
		CriarTecnica(pl);
		TipoDaTecnica(pl, "guided");
		TextoDaMesa(pl, "nome/Rastreadora");
		TextoDaMesa(pl, "desc/Persegue quem eu marcar.");
		TextoDaMesa(pl, "grito/Nao adianta correr!");
		AlternarGrito(pl, "grito");
		// QUATRO de potencia primeiro: o `KiMais` devolve 1 e o `StaminaLigar` devolve 2, e desde o piso
		// em zero eles precisam ter esses pontos gastos pra sequer passar. Sem isso os dois seriam
		// recusados e a tecnica iria pro disco SEM folego -- e a afirmacao de baixo, que compara ida com
		// volta, ficaria verde comparando dois "false".
		for (int i = 0; i < 4; i++) ComprarNaMesa(pl, nameof(Compra.DanoMais));
		ComprarNaMesa(pl, nameof(Compra.KiMais));
		ComprarNaMesa(pl, nameof(Compra.StaminaLigar));
		AfirmarTc("a que vai pro disco tem folego, Ki caro e saldo 1 -- nenhum campo no valor de um `new`",
				  pl.Mesa is { UsaStamina: true, Gasto: 1 } && Math.Abs(pl.Mesa.CustoKi - 60) < 1e-9,
				  pl.Mesa == null ? "mesa fechada" : $"gasto {pl.Mesa.Gasto} folego {pl.Mesa.UsaStamina}");
		SalvarTecnica(pl);

		TecnicaCustomizada antes = pl.Customizadas[0];

		string pasta = Path.Combine(Path.GetTempPath(), "jandirus_tecnicas_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_tecnicas" };
			conta.Slots[0] = AccountStore.DeJogador(pl, 0);
			loja.Gravar(conta);

			CharacterSave? volta = loja.Carregar("bancada_tecnicas")?.Slots[0];
			AfirmarTc("a conta e o slot voltam do disco", volta != null);
			AfirmarTc("...com a lista de tecnicas dentro (a linha em `DeJogador` existe)",
					  volta?.Customizadas is { Count: 1 },
					  volta?.Customizadas == null ? "nulo" : $"{volta.Customizadas.Count}");

			TecnicaCustomizada? depois = volta?.Customizadas?[0];
			AfirmarTc("...com o TIPO, o nome, a descricao e o grito intactos",
					  depois != null && depois.Tipo == antes.Tipo && depois.Nome == antes.Nome
					  && depois.Desc == antes.Desc && depois.Grito == antes.Grito && depois.DizGrito,
					  depois == null ? "nulo" : $"{depois.Tipo}/{depois.Nome}/{depois.Grito}");
			AfirmarTc("...e com os NUMEROS e os PONTOS GASTOS, que e o que doi perder",
					  depois != null && Math.Abs(depois.BaseDano - antes.BaseDano) < 1e-9
					  && Math.Abs(depois.CustoKi - antes.CustoKi) < 1e-9
					  && depois.UsaStamina == antes.UsaStamina
					  && Math.Abs(depois.CustoStamina - antes.CustoStamina) < 1e-9
					  && depois.Gasto == antes.Gasto,
					  depois == null ? "nulo" : $"dano {depois.BaseDano} ki {depois.CustoKi} gasto {depois.Gasto}");

			// A VOLTA PELO CAMINHO DE PRODUCAO: e `PrepararCustomizadas` que le, e nao o teste.
			var renascido = new ServerPlayer { Id = 1, Ficha = pl.Ficha };
			PrepararCustomizadas(renascido, volta);
			AfirmarTc("...e o `PrepararCustomizadas` a devolve pro corpo, marcada como pronta",
					  renascido.Customizadas.Count == 1 && renascido.Customizadas[0].Criada
					  && renascido.Customizadas[0].Verbo == "Custom_Attack1",
					  $"{renascido.Customizadas.Count}");

			// ============ SAVE ANTIGO: CAMPO NULO, E SEM RAMO DE MIGRACAO ============
			// O save escrito antes deste sistema nao tem o campo. Ele tem que entrar como quem nunca
			// inventou nada -- que e a MESMA coisa --, e nao explodir num `foreach` sobre nulo.
			var antigo = new CharacterSave { Nome = "Velho" };
			var velho = new ServerPlayer { Id = 2, Ficha = pl.Ficha };
			PrepararCustomizadas(velho, antigo);
			AfirmarTc("save ANTIGO (campo nulo) entra sem migracao e sem tecnica nenhuma",
					  velho.Customizadas.Count == 0);

			// E O JSON DELE NAO GANHA LIXO: sem tecnica, o campo sai NULO do `DeJogador` -- que e o
			// que mantem o save de quem nunca abriu a mesa exatamente como era.
			AfirmarTc("...e quem nao inventou nada continua com o campo nulo no disco",
					  AccountStore.DeJogador(velho, 0).Customizadas == null);
		}
		catch (Exception e)
		{
			AfirmarTc("as tecnicas atravessam o disco", false, e.Message);
		}
		finally
		{
			try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.Print($"[tecnica] nao consegui apagar {pasta}: {e.Message}"); }
		}

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5) AS TRES DISPARAM
	// =====================================================================
	/// <summary>
	/// O TIRO, PELO MOTOR DA CAMADA 1. Nao ha um segundo projetil: a receita da tecnica entra pelo
	/// mesmo <c>Disparar</c>/<c>Canalizar</c> que o `Ki_Wave` e o `Basic_Blast` usam.
	/// </summary>
	private void AsTresDisparam()
	{
		GD.Print("[tecnica] -- 5) AS TRES DISPARAM, E OS NUMEROS COMPRADOS CHEGAM NO TIRO");

		// ---------------------------------------------------------------- bola
		Vec2 chao = CorredorLivre(24);
		ServerPlayer pl = Forjar("Atirador", chao, bp: 5_000);
		pl.Facing = Facing.East;

		CriarTecnica(pl);
		TipoDaTecnica(pl, "blast");
		TextoDaMesa(pl, "nome/Bola Pesada");
		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		ComprarNaMesa(pl, nameof(Compra.DanoMais));
		ComprarNaMesa(pl, $"{nameof(Compra.VelocidadeMais)}");
		SalvarTecnica(pl);

		TecnicaCustomizada bola = pl.Customizadas[0];
		double kiAntes = pl.Ficha.Ki;
		int vivosAntes = ProjeteisDaZona(pl.Zone.Hash).Count;

		AfirmarTc("o verbo dela e `Custom_Attack1` e o despacho o reconhece",
				  UsarTecnicaCustomizada(pl, "Custom_Attack1"));

		List<Projetil> lista = ProjeteisDaZona(pl.Zone.Hash);
		AfirmarTc("...e um projetil de verdade nasceu", lista.Count == vivosAntes + 1);
		Projetil p = lista[^1];

		AfirmarTc("...do tipo comprado, com o nome que o jogador deu",
				  p.Tipo == TipoDeProjetil.Blast && p.Nome == "Bola Pesada", $"{p.Tipo}/{p.Nome}");
		AfirmarTc("...com o `base_damage` de 1,0 que ele pagou 2 pontos pra ter",
				  Math.Abs(p.BaseDano - 1.0) < 1e-9, $"{p.BaseDano}");
		AfirmarTc("...e com a VELOCIDADE 2 virando o atraso do DM (`max(1, round(4-speed))`)",
				  Math.Abs(p.SegundosPorTile - Projetil.AtrasoDeBola(2)) < 1e-9,
				  $"{p.SegundosPorTile:0.###} vs {Projetil.AtrasoDeBola(2):0.###}");
		AfirmarTc("...e o Ki cobrado foi `ki_cost * BaseDrain` (`:410`)",
				  Math.Abs((kiAntes - pl.Ficha.Ki) - bola.CustoKi * pl.Ficha.BaseDrain()) < 1e-6,
				  $"tirou {kiAntes - pl.Ficha.Ki:0.##}");

		// A BOLA ACERTA, E QUEM DECIDE E O SERVIDOR -- pelo mesmo `Acertar` da camada 1.
		ServerPlayer vitima = Forjar("Vitima", new Vec2(p.Pos.X + 200, chao.Y), bp: 500);

		// ============================ O DADO DA DEFLEXAO SAI DE CIMA DA MESA ============================
		// ATE 2026-09-02 ESTA PROVA ERA UMA MOEDA, e ela caiu: uma rodada de dez saiu vermelha com
		// `fim Defletido / vida 100 -> 100` e as tres seguintes deram verde. Nao havia defeito nenhum
		// no jogo -- o que a linha de baixo estava medindo era o SORTEIO DE DEFLEXAO DE PRODUCAO.
		//
		// ---- ONDE MORA O DADO ----
		// `GameServer.Projeteis.cs:1190-1240`, dentro do `Acertar`: a chance sai de
		// `DanoDeKi.ChanceDeDeflexao` (`Core/Combat/DanoDeKi.cs:170`), que e
		//
		//     (Ekidef * max(expressedBP,1) * max(Ekiskill,Etechnique) * max(kidefenseskill/10,1))
		//     / (BP_do_tiro * mods * basedamage)                            (`objects.dm:333`)
		//
		// em PORCENTO, sorteada DUAS vezes por impacto: `prob(chance/2)` e a deflexao barata (o corpo
		// sai da linha e o tiro segue) e `prob(chance)` e a cara, que MATA o tiro com
		// `FimDeProjetil.Defletido`. Ela dispara em todo impacto contra quem NAO esta nocauteado nem
		// atordoado e tem 5 de Ki -- ou seja, contra a vitima desta prova, que e um corpo inteiro de pe.
		// MEDIDO com os corpos exatos daqui (atirador 5.000, vitima 500, `base_damage` 1,0): 0,0999%
		// por impacto, e o laco de sub-passos do `AndarProjetil` testa a colisao umas seis vezes na
		// janela de 16 px -- da meio por cento por rodada, que e exatamente a frequencia observada.
		//
		// ---- POR QUE DESLIGAR ASSIM, E NAO DE OUTRO JEITO ----
		// O precedente e o `RaioDaBancada` (`GameServer.ProjeteisTeste.cs:2167`): a receita da bancada
		// nasce com `Deflectivel = false` *"porque um sorteio no meio da medicao mediria o dado"* -- o
		// mesmo motivo pelo qual o `Dupla` (`GameServer.BancadaComum.cs:124`) desliga o bloqueio.
		// Aqui a RECEITA nao e nossa: ela sai de `TecnicaCustomizada.Receita()`, e o comentario de la
		// (`Core/Skills/TecnicaCustomizada.cs:258`) diz por que `Deflectivel` fica no padrao -- o painel
		// do DM nao vende esse botao, e escrever `false` la daria a todo jogador uma tecnica que
		// ninguem consegue defletir. Entao o knob e virado no PROJETIL desta prova, que e o mais perto
		// que da pra chegar do precedente sem mexer em producao: mesmo campo, mesma razao, so no unico
		// lugar que a bancada alcanca.
		//
		// O CRITERIO DA AFIRMACAO NAO MUDOU: ela continua exigindo `!Vivo && Fim == Acertou && vida
		// caiu`. O que mudou foi de onde vem a resposta -- do jogo, e nao do dado.
		//
		// A CHANCE CRUA E LIDA ANTES, e ela e a prova de que isto NAO afrouxou nada: se um dia ela
		// vier zero, e porque a vitima deixou de saber defender (corpo de bancada diferente do corpo
		// do jogo), e a linha do placar la embaixo fica vermelha por isso.
		// ==============================================================================================
		double chanceCrua = DanoDeKi.ChanceDeDeflexao(vitima.Ficha, p.Bp, p.ModsAgora(), p.BaseDano,
													  vitima.Combate.Bloqueando);
		p.Deflectivel = false;

		double vidaAntes = vitima.Combate.Corpo.Vida();
		List<string> noVoo = Ouvir(() =>
		{
			for (int i = 0; i < 300 && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		});
		AfirmarTc("...ela morre em quem acerta, e a vitima perde vida",
				  !p.Vivo && p.Fim == FimDeProjetil.Acertou && vitima.Combate.Corpo.Vida() < vidaAntes,
				  $"fim {p.Fim} / vida {vidaAntes:0.#} -> {vitima.Combate.Corpo.Vida():0.#}");

		// A LINHA QUE PROVA O MECANISMO, e nao "nao caiu nesta rodada". Ela junta as tres coisas que
		// tornam o caminho da deflexao INALCANCAVEL com esta receita:
		//   1. o dado EXISTE (`chanceCrua > 0`): a vitima nao foi enfraquecida pra prova passar;
		//   2. o projetil que voou tinha `Deflectivel = false`, e com ele o `Acertar` zera a chance
		//      na porta (`GameServer.Projeteis.cs:1192`);
		//   3. `Sorteio(0)` e falso pra QUALQUER rolagem -- o `porcento > 0` corta antes de tocar no
		//      `_rng` (`GameServer.Projeteis.cs:1567`). Chamar o sorteio de producao com zero nao
		//      consome numero nenhum do gerador, entao esta linha nao move o dado das provas seguintes.
		// E a SONDA de runtime junto: nenhuma das tres falas que a deflexao escreve (`voce defletiu`,
		// `de raspao`, `defletiu seu ataque`) saiu durante o voo. As duas metades sao necessarias --
		// a fala sozinha seria so mais uma rodada; o knob sozinho nao mostraria que o caminho calou.
		AfirmarTc($"...e o DADO DA DEFLEXAO estava desligado PELO MECANISMO: a chance crua era "
				+ $"{chanceCrua:0.####}% por impacto e `Deflectivel = false` a zera na porta do `Acertar`",
				  chanceCrua > 0 && !p.Deflectivel && !Sorteio(0)
				  && !Disse(noVoo, "defletiu") && !Disse(noVoo, "raspao"),
				  $"chance {chanceCrua:0.#####}% / deflectivel {p.Deflectivel} / falas: {string.Join(" | ", noVoo)}");

		LimparTudoDaBancada();

		// ---------------------------------------------------------------- raio
		chao = CorredorLivre(24);
		pl = Forjar("Raiador", chao, bp: 5_000);
		pl.Facing = Facing.East;
		CriarTecnica(pl);
		TextoDaMesa(pl, "nome/Raio Comprido");
		ComprarNaMesa(pl, $"{nameof(Compra.Alcance)}/25");
		SalvarTecnica(pl);

		AfirmarTc("o raio nasce como CANAL, e nao como tiro (a carga do DM)",
				  UsarTecnicaCustomizada(pl, "Custom_Attack1") && _canais.ContainsKey(pl.Id)
				  && ProjeteisDaZona(pl.Zone.Hash).Count == 0);
		AfirmarTc("...e enquanto carrega o corpo fica PLANTADO (o `canmove = 0`, `beams.dm:294`)",
				  !PodeMexerOCorpo(pl));

		for (int i = 0; i < 200 && ProjeteisDaZona(pl.Zone.Hash).Count == 0; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		lista = ProjeteisDaZona(pl.Zone.Hash);
		AfirmarTc("...depois da carga o raio SAI", lista.Count == 1, $"{lista.Count}");
		if (lista.Count == 1)
		{
			Projetil r = lista[0];
			AfirmarTc("...com o alcance de 25 tiles que custou 5 pontos",
					  Math.Abs(r.MaxDistancia - 25) < 1e-9, $"{r.MaxDistancia}");
			AfirmarTc("...e com o atraso de RAIO (`beamspeed = 1/speed`), que nao e o da bola",
					  Math.Abs(r.SegundosPorTile - Projetil.AtrasoDeRaio(1)) < 1e-9,
					  $"{r.SegundosPorTile:0.###}");
		}

		// APERTAR DE NOVO SOLTA -- o `if(beaming) stopbeaming()` de todo verbo de raio.
		AfirmarTc("apertar de novo solta o raio e devolve o corpo",
				  UsarTecnicaCustomizada(pl, "Custom_Attack1") && !_canais.ContainsKey(pl.Id)
				  && PodeMexerOCorpo(pl));

		LimparTudoDaBancada();

		// ---------------------------------------------------------------- teleguiado
		chao = CorredorLivre(24);
		pl = Forjar("Cacador", chao, bp: 5_000);
		pl.Facing = Facing.East;
		ServerPlayer presa = Forjar("Presa", new Vec2(chao.X + 300, chao.Y), bp: 500);
		pl.AlvoId = presa.Id;

		CriarTecnica(pl);
		TipoDaTecnica(pl, "guided");
		TextoDaMesa(pl, "nome/Farejadora");
		SalvarTecnica(pl);
		UsarTecnicaCustomizada(pl, "Custom_Attack1");

		lista = ProjeteisDaZona(pl.Zone.Hash);
		AfirmarTc("o teleguiado sai marcando o alvo escolhido",
				  lista.Count == 1 && lista[0].Alvo == presa.Id,
				  lista.Count == 0 ? "nao saiu" : $"alvo {lista[0].Alvo}");

		// ---------------------------------------------------------------- o folego, que no DM nao valia
		// Ver a nota de `AtirarRaioCustom`: o `CustomShotOK` do original nunca e chamado pelo beam,
		// entao ligar estamina num raio la e dois pontos de graca. Aqui cobra.
		LimparTudoDaBancada();
		pl = Forjar("Cansado", CorredorLivre(4), bp: 5_000);
		CriarTecnica(pl);
		TextoDaMesa(pl, "nome/Raio Suado");
		// O FOLEGO E TODO ESTORNO (2 pra ligar + 1 por degrau), entao ele abre com o orcamento inteiro
		// gasto -- e sai daqui com ele zerado. Sem os 4 primeiros, o piso recusaria os tres cliques e
		// o raio nao gastaria folego nenhum.
		for (int i = 0; i < 4; i++) ComprarNaMesa(pl, nameof(Compra.DanoMais));
		ComprarNaMesa(pl, nameof(Compra.StaminaLigar));
		ComprarNaMesa(pl, nameof(Compra.StaminaMais));
		ComprarNaMesa(pl, nameof(Compra.StaminaMais));   // 3 de folego, gasto 0
		SalvarTecnica(pl);

		double folegoAntes = pl.Ficha.stamina;
		UsarTecnicaCustomizada(pl, "Custom_Attack1");
		AfirmarTc("o RAIO tambem cobra folego (no DM o `CustomShotOK` nunca chegava no beam)",
				  Math.Abs((folegoAntes - pl.Ficha.stamina) - 3) < 1e-6,
				  $"tirou {folegoAntes - pl.Ficha.stamina:0.##}");

		FecharCanal(pl.Id, _canais[pl.Id], null);
		pl.Ficha.stamina = 1;
		EscutaDeAvisos = [];
		UsarTecnicaCustomizada(pl, "Custom_Attack1");
		AfirmarTc("...e sem folego ele e recusado com a frase do DM (`:488`)",
				  !_canais.ContainsKey(pl.Id)
				  && EscutaDeAvisos.Exists(a => a.Contains("cansado", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) O DESPACHO
	// =====================================================================
	private void ODespachoDoVerbo()
	{
		GD.Print("[tecnica] -- 6) O VERBO CHEGA, E O SLOT VAZIO E RECUSADO DIZENDO POR QUE");

		AfirmarTc("`Custom_Attack7` vira o id 7, e `Custom_Attack11` nao vira nada (o teto de 10)",
				  TecnicaCustomizada.IdDoVerbo("Custom_Attack7") == 7
				  && TecnicaCustomizada.IdDoVerbo("Custom_Attack11") == 0
				  && TecnicaCustomizada.IdDoVerbo("Ki_Wave") == 0
				  && TecnicaCustomizada.IdDoVerbo("Custom_Attack0") == 0);

		ServerPlayer pl = Forjar("Vazio", CorredorLivre(4), bp: 5_000);
		EscutaDeAvisos = [];

		AfirmarTc("um verbo que nao e custom passa direto (nao e daqui)",
				  !UsarTecnicaCustomizada(pl, "Kamehameha"));

		AfirmarTc("um slot VAZIO e reconhecido como custom e recusado (o DM saia calado, `:402`)",
				  UsarTecnicaCustomizada(pl, "Custom_Attack3")
				  && EscutaDeAvisos.Count > 0
				  && ProjeteisDaZona(pl.Zone.Hash).Count == 0,
				  string.Join(" | ", EscutaDeAvisos));

		// UM RASCUNHO NAO ATIRA. A tecnica so existe depois do "Done" -- antes disso ela e uma
		// sombra, e uma sombra que dispara seria uma tecnica de graca (sem confirmar, sem gravar).
		CriarTecnica(pl);
		EscutaDeAvisos.Clear();
		UsarTecnicaCustomizada(pl, "Custom_Attack1");
		AfirmarTc("...e um RASCUNHO na mesa tambem nao atira",
				  ProjeteisDaZona(pl.Zone.Hash).Count == 0 && !_canais.ContainsKey(pl.Id));
		CancelarMesa(pl);

		// O COMANDO DESCONHECIDO COM PREFIXO `ca_` E DAQUI, e responde. Um `ca_qualquercoisa` que
		// caisse no `default` do `Verbo()` iria parar na guarda de admin e sairia calado.
		EscutaDeAvisos.Clear();
		AfirmarTc("um `ca_` desconhecido e reconhecido e respondido, em vez de sumir",
				  ComandoDeTecnicaCustomizada(pl, "ca_naoexiste", "") && EscutaDeAvisos.Count > 0,
				  string.Join(" | ", EscutaDeAvisos));
		AfirmarTc("...e um comando de outro sistema nao e capturado por engano",
				  !ComandoDeTecnicaCustomizada(pl, "quem", ""));

		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}
}
