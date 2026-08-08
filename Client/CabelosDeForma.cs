using Godot;

namespace Jandirus.Client;

/// <summary>
/// QUAL SPRITE DE CABELO UMA FORMA USA -- o `/obj/overlay/hairs/ssj/ssj1..3` do BYOND.
///
/// ============================ O CABELO NAO E SO COR ============================
/// Este arquivo existe por um defeito meu: eu portei a escada inteira tratando o cabelo como
/// TINTA. Dourar o penteado do Goku deixa ele igual, so amarelo -- e o Super Saiyajin nao tem o
/// mesmo penteado, ele tem o cabelo EM PE. O BYOND troca o overlay inteiro (`removeOverlay(hair)` +
/// `updateOverlay(ssj/ssj1)`), e a pasta `SSJ Hairs/` do projeto ja tinha 57 variantes esperando
/// alguem usar.
///
/// ============================ E TROCAR TAMBEM NAO E A RESPOSTA INTEIRA ============================
/// A varredura das formas achou o outro lado do mesmo erro: ha formas que **nao trocam nada e so
/// pintam** (o SSG poe `rgb(226,51,28)` no penteado base), ha formas que fazem **as duas coisas** (o
/// Legendary poe o sprite de USSJ e soma verde por cima) e ha uma que faz **uma OU outra** (o Ultra
/// Instinct). Este arquivo continua respondendo so a primeira pergunta -- QUAL SPRITE --, e quem
/// decide se ainda cabe tinta e `Catalogo.ModoDoCabelo`, no Core.
/// ==============================================================================================
///
/// ============================ POR QUE UM RESOLVEDOR E NAO UMA TABELA ============================
/// A nomenclatura da pasta e IRREGULAR -- ela veio de anos de gente diferente desenhando:
///
///     Hair_Goku      -> Hair_GokuSSj, Hair_GokuSSj3, Hair_GokuSSjFP, Hair_GokuUSSj
///     Hair_Caulifla  -> Caulifla_Hair_SSJ, Caulifla_Hair_SSJ2, Caulifla_Hair_USSJ
///     Hair Afro      -> Hair AfroSSJ
///     HairPonytail   -> HairPonytailSSJ, HairPonytailSSJFP
///     Hair_Kale      -> Kale Hair SSJ, Kale Hair LSSJ
///
/// Uma tabela escrita a mao com 57 linhas envelheceria no primeiro cabelo novo. O resolvedor tenta
/// os PADROES conhecidos e, se nenhum casar, devolve nulo -- e ai o cabelo base fica e so recebe a
/// tinta, que e o comportamento de antes. Nenhum penteado fica quebrado por falta de variante.
/// =========================================================================================
/// </summary>
public static class CabelosDeForma
{
	private const string Pasta = "res://Assets/Sprites/Hair/SSJ Hairs/";

	/// <summary>
	/// A PASTA DOS PENTEADOS NORMAIS. O Ultra Instinct mora AQUI e nao em `SSJ Hairs/` -- e onde a
	/// conversao dos `.dmi` pos o `Hair_UltraInstinct` e o `Hair_MasteredUltraInstinct`, porque no DM
	/// eles tambem nao estao na pasta de Super Saiyajin (`UltraInstinct.dm:279`, `:285`).
	/// </summary>
	private const string PastaBase = "res://Assets/Sprites/Hair/";

	/// <summary>Cache do que ja foi resolvido. A busca toca o disco; a transformacao nao pode.</summary>
	private static readonly Dictionary<string, string?> _cache = [];

	/// <summary>
	/// O SPRITE DE CABELO DESTA FORMA, ou nulo quando este penteado nao tem variante.
	///
	/// <paramref name="baseDoCabelo"/> e o caminho do `.tres` do cabelo normal (o que o
	/// <see cref="VisualCatalog"/> devolve). <paramref name="sufixo"/> vem do
	/// <see cref="Jandirus.Core.Forms.FormaDef.SufixoDoCabelo"/>.
	/// </summary>
	public static string? De(string? baseDoCabelo, string sufixo, bool feminino = false)
	{
		if (string.IsNullOrEmpty(baseDoCabelo) || string.IsNullOrEmpty(sufixo)) return null;

		string chave = $"{baseDoCabelo}|{sufixo}|{(feminino ? "f" : "m")}";
		if (_cache.TryGetValue(chave, out string? achado)) return achado;

		string nome = baseDoCabelo.GetFile().GetBaseName();   // "Hair_Goku"
		achado = Universal(nome, sufixo, feminino) ?? Procurar(nome, sufixo) ?? Herdar(nome, sufixo);
		_cache[chave] = achado;
		return achado;
	}

	/// <summary>
	/// ALGUMAS FORMAS TEM CABELO UNICO, IGUAL PRA TODO MUNDO -- e nao uma variante por penteado.
	///
	/// ============================ O DEFEITO QUE ISTO CONSERTA ============================
	/// O SSJ4 saiu com o cabelo do SSJ1. O motivo: eu tratei TODA forma como "penteado + sufixo",
	/// procurei `Hair_GokuSSJ4`, nao achei (nao existe) e cai na heranca, que devolveu o do SSJ1.
	///
	/// Mas a arte do jogo diz outra coisa, e o dono confirmou a regra: o SSJ4 tem cabelo PROPRIO,
	/// igual pra quase todo mundo. No canone o cabelo do SSJ4 e o mesmo -- comprido e escuro -- e
	/// nao o penteado da pessoa arrepiado. So o Vegeta tem desenho proprio, porque o widow's peak
	/// dele sobrevive a transformacao.
	/// ================================================================================
	/// </summary>
	private static string? Universal(string nome, string sufixo, bool feminino)
	{
		// ============================ O ULTRA INSTINCT E O SEGUNDO CASO, E E MAIS ESTRITO ============================
		// O SSJ4 tem arte pra todo mundo (uma folha masculina, uma feminina e a do Vegeta). O Ultra
		// Instinct tem arte pra UMA pessoa: `ui_apply_hair()` (`UltraInstinct.dm:296-303`) so troca o
		// cabelo `if(hairtypeSaved == "Goku")`, e quem nao e Goku recebe outro overlay -- o base, no
		// Sign, ou o base prateado, no Perfected.
		//
		// Por isso este ramo devolve NULO pra qualquer outro penteado em vez de cair no `Procurar`: ele
		// nao acharia nada de qualquer jeito (nao existe `Hair_VegetaUI`), mas a `Herdar` acharia -- e
		// um Ultra Instinct com o cabelo espetado de Super Saiyajin seria pior que nenhum. Devolver
		// nulo aqui e o que faz a tinta prateada do `ModoDoCabelo.TrocarOuTingir` entrar no lugar.
		// ==========================================================================================================
		if (sufixo is Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstinto
					or Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito)
		{
			if (!nome.Contains("Goku", StringComparison.OrdinalIgnoreCase)) return null;
			string ui = PastaBase + (sufixo == Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito
									 ? "Hair_MasteredUltraInstinct" : "Hair_UltraInstinct") + ".tres";
			return ResourceLoader.Exists(ui) ? ui : null;
		}

		if (!sufixo.Equals("SSJ4", StringComparison.OrdinalIgnoreCase)) return null;

		// A REGRA, ditada pelo dono e conferida na pasta:
		//   mulher, qualquer penteado -> Hair_SSJ4Female
		//   homem com cabelo de Vegeta -> Hair VegetaSSJ4
		//   homem, todo o resto        -> Hair_SSj4
		//
		// "Cabelo de Vegeta" pega `Hair_Vegeta`, `Hair_GTVegeta` e `Hair Vegeta Junior`: os tres
		// sao o mesmo formato de widow's peak, e e ele que ganha o desenho proprio de SSJ4.
		string arquivo = feminino ? "Hair_SSJ4Female"
					   : nome.Contains("Vegeta", StringComparison.OrdinalIgnoreCase) ? "Hair VegetaSSJ4"
					   : "Hair_SSj4";

		string caminho = $"{Pasta}{arquivo}.tres";
		return ResourceLoader.Exists(caminho) ? caminho : null;
	}

	/// <summary>
	/// Tenta os padroes conhecidos. A ordem importa pouco (so um casa), mas a COBERTURA importa
	/// muito -- cada padrao aqui e um punhado de penteados que passam a funcionar.
	/// </summary>
	private static string? Procurar(string nome, string sufixo)
	{
		// "Hair_Goku" -> "Goku"; "HairPonytail" -> "Ponytail"; "Hair Afro" -> "Afro"
		string curto = nome;
		foreach (string prefixo in new[] { "Hair_", "Hair ", "Hair" })
			if (curto.StartsWith(prefixo, StringComparison.Ordinal)) { curto = curto[prefixo.Length..]; break; }

		// ============================ HA PENTEADO COM "Hair" NO FIM, E NAO SO NO COMECO ============================
		// Achado medindo a pasta penteado por penteado (a varredura do pedido do `fp`): tres bases do
		// catalogo terminam em " Hair" em vez de comecarem com ele -- `Inferno Hair`, `Mezu Hair` e
		// `Hope FFXIII Hair`. O corte de prefixo la de cima nao encosta neles, entao `curto` continuava
		// sendo "Inferno Hair" e nenhum dos seis padroes casava.
		//
		// O CUSTO ERA TRES FOLHAS MORTAS: `Inferno SSJ`, `Inferno USSJ` e `Inferno SSJFP` estao
		// desenhadas na pasta e nunca foram pedidas por ninguem. A do FP e a que trouxe o assunto --
		// ela e uma das dez de Full Power, e sem esta linha o pedido do dono chegaria a nove penteados
		// tendo arte pra dez.
		//
		// NAO HA COMO ISTO CASAR ERRADO: o padrao so entra na lista de tentativas e cada tentativa e
		// conferida contra o DISCO (`ResourceLoader.Exists`) com o nome inteiro do arquivo. "Mezu SSJ" e
		// "Hope FFXIII SSJ" simplesmente nao existem, e nada acontece pra esses dois.
		// ======================================================================================================
		string semHair = curto.EndsWith(" Hair", StringComparison.Ordinal) ? curto[..^5] : curto;

		// AS VARIACOES DE CAIXA sao necessarias: a pasta tem `SSj`, `SSJ` e `Ssj` no mesmo lugar.
		string[] casos = [sufixo, sufixo.ToUpperInvariant(), sufixo.ToLowerInvariant()];

		foreach (string s in casos)
		{
			string?[] tentativas =
			[
				$"{nome}{s}",              // Hair_GokuSSj, Hair AfroSSJ, HairPonytailSSJ
				$"{nome}_{s}",             // Hair_Goku_SSj
				$"{curto}_Hair_{s}",       // Caulifla_Hair_SSJ
				$"{curto} Hair {s}",       // Kale Hair SSJ
				$"Hair_{curto}{s}",        // Hair_VegetaSSj (quando o base e "Vegeta")
				$"Hair {curto}{s}",        // Hair VegetaSSJ4
				$"{semHair} {s}",          // Inferno SSJ, Inferno SSJFP (base "Inferno Hair")
			];
			foreach (string? t in tentativas)
			{
				string caminho = $"{Pasta}{t}.tres";
				if (ResourceLoader.Exists(caminho)) return caminho;
			}
		}
		return null;
	}

	/// <summary>
	/// SEM VARIANTE PROPRIA, HERDA A DE BAIXO.
	///
	/// A pasta e desigual: o Goku tem SSj, SSj3 e SSjFP mas NAO tem SSj2; quase ninguem tem SSJ4.
	/// Sem esta cadeia, virar SSJ2 com cabelo do Goku voltaria o penteado ao normal -- um passo
	/// ATRAS no meio da escada, que le como defeito.
	///
	/// A cadeia vai sempre pra um degrau MAIS BAIXO, nunca mais alto: e melhor o SSJ2 usar o
	/// cabelo do SSJ1 do que o SSJ1 usar o do SSJ3.
	/// </summary>
	private static string? Herdar(string nome, string sufixo) => sufixo switch
	{
		"SSj4" or "SSJ4" => Procurar(nome, Jandirus.Core.Forms.Catalogo.SufixoDoSuperSaiyajinPleno)
						 ?? Procurar(nome, "SSj3") ?? Procurar(nome, "SSj"),

		// ============================ O FULL POWER CAI NO SSJ1 E EM MAIS NADA ============================
		// Isto era `Procurar(nome, "SSj3") ?? Procurar(nome, "SSj")`, e enquanto o sufixo `SSjFP` era
		// inalcancavel (nenhuma entrada do catalogo o pedia) a linha nao fazia mal a ninguem. Hoje ela
		// e o caminho de 48 dos 58 penteados jogaveis com sprite (o catalogo tem 59 entradas; a que
		// falta e o `Bald`, que nao tem sprite nenhum), e o salto pelo `SSj3` estaria ERRADO em todos
		// eles: quem
		// dominasse o Super Saiyajin com cabelo do Goku viraria Grade 4 com o CABELO DE SSJ3 -- um
		// degrau que ele pode nem ter despertado. Regra do dono: *"pede o fp, e se nao houver, cai no
		// cabelo normal da forma"*, e o cabelo normal desta forma e o de Super Saiyajin.
		//
		// (O ramo do `SSJ4` logo acima continua passando pelo FP de proposito: la o FP nao e o topo da
		// escada e sim o degrau MAIS ALTO ABAIXO do SSJ4 -- a cadeia continua andando pra baixo, que e
		// a regra desta funcao. E ele so roda se a arte universal de SSJ4 sumir do disco.)
		// =============================================================================================
		Jandirus.Core.Forms.Catalogo.SufixoDoSuperSaiyajinPleno => Procurar(nome, "SSj"),
		"SSj3" => Procurar(nome, "SSj2") ?? Procurar(nome, "SSj"),
		// ============================ O SSJ2 TEM DEGRAU PROPRIO: O `USSj` ============================
		// Isto caia direto no cabelo do SSJ1, e o dono viu: "o cabelo do goku no ssj2 ta errado, o
		// ssj2 usa o cabelo Hair_GokuUSSj.png".
		//
		// Nao e caso especial dele: SETE penteados do catalogo acham variante `USSj` -- Caulifla,
		// Gohan, Goku, KidGohan, Long, Vegeta e Inferno. (Esta conta ja disse OITO e incluia o
		// Trunks: a folha `Hair_TrunksUSSj` existe na pasta, mas nenhum penteado JOGAVEL se chama
		// `Hair_Trunks` -- o do catalogo e o `Hair_GTTrunks` --, entao ela nao e alcancavel por
		// ninguem. Medido penteado a penteado na varredura do `fp`.) O sufixo veio do Ultra Super
		// Saiyajin do DM, mas a arte e a mesma coisa que o SSJ2 precisa: o cabelo mais erguido e
		// espesso do degrau seguinte. Estava desenhada e ninguem pedia.
		//
		// A cadeia certa e USSj -> SSj: quem nao tem o intermediario continua caindo no SSJ1, que e
		// melhor que voltar ao penteado normal no meio da escada.
		"SSj2" => Procurar(nome, "USSj") ?? Procurar(nome, "SSj"),
		"USSj" => Procurar(nome, "SSj"),

		// ============================ O LEGENDARY HERDA DO USSJ, E ISSO E LITERAL ============================
		// Caia direto no `SSj`, e o degrau do meio nao era enfeite: `HairObject.dm:215` passa
		// `container.ussjhair` como fonte -- `updateOverlay(/obj/overlay/hairs/ssj/lssjhair, ussjhair,
		// 0,100,0)`. O sprite do Legendary E o do USSJ, verde por cima.
		//
		// A folha propria de Legendary existe pra TRES penteados (`HairChoose.dm:338`, `:340`, `:342`:
		// Broly, FemBroly e Kale) e o `Procurar` acha os tres pelo nome. Os outros ~59 sao o USSJ
		// pintado -- que e exatamente o que o DM faz com eles, e nao uma degradacao nossa.
		// ==================================================================================================
		"LSSj" => Procurar(nome, "USSj") ?? Procurar(nome, "SSj"),

		// O ULTRA INSTINCT NAO HERDA NADA. Ver o ramo dele no `Universal`: quem nao e Goku fica com o
		// penteado proprio e recebe a prata por tinta. Herdar aqui poria cabelo de Super Saiyajin
		// numa forma que no original nem ergue o cabelo.
		Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstinto
			or Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito => null,

		_ => null,
	};

	/// <summary>Quantos penteados tem variante pra este sufixo. Pra bancada -- ver `--diagforma`.</summary>
	public static (int Com, int Sem) Cobertura(IEnumerable<string> basesDeCabelo, string sufixo)
	{
		int com = 0, sem = 0;
		foreach (string b in basesDeCabelo)
			if (De(b, sufixo) != null) com++; else sem++;
		return (com, sem);
	}
}
