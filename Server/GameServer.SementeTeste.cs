using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA SEMENTE DO UNIVERSO -- `--sementeteste`.
///
/// ============================ O QUE ELA MEDE, E POR QUE CADA FAMILIA EXISTE ============================
/// O pedido do dono tem duas metades que se contradizem se lidas rapido: *"os NPCS estao sempre
/// nascendo IGUAIS a cada WIPE e o UNIVERSO tb"* -- ou seja, MUDE o mundo -- convivendo com a lei da
/// casa, que e **a mesma semente da o mesmo mundo**. As duas so convivem por causa de ONDE a semente
/// nasce, e e exatamente isso que esta bancada afirma, familia por familia:
///
///   1. MUNDO NOVO SORTEIA -- pasta vazia vira um universo com endereco proprio, gravado.
///   2. REINICIAR NAO MUDA -- reler o disco N vezes devolve o MESMO mundo. E a lei.
///   3. OUTRO ENDERECO, OUTRO UNIVERSO -- a contraprova da familia 2. Sem ela, "o mundo continua o
///      mesmo" ficaria verde num servidor que ignorasse a semente por completo.
///   4. O WIPE ALCANCA A SEMENTE -- o arquivo some com a vassoura, a memoria ja nasce noutro
///      universo, e a producao grava o endereco novo. **E esta a familia do pedido.**
///   5. MUNDO VELHO NAO TROCA DE SEMENTE -- save que ja existe e nao tem `universo.json` fica com a
///      <see cref="GameServer.SementeLegada"/>. Sem isto, esta mudanca trocaria o mundo do dono
///      debaixo dele: as contas guardam `ZonaSeed`, as obras guardam a zona onde estao de pe e os
///      dominios guardam o endereco `(Sx, Sy, K)` do planeta.
///   6. O FORMATO -- largura fixa, porque o retrato da `--wipeteste` compara TAMANHO de arquivo.
///   7. DETERMINISMO COM SEMENTE ESCRITA AQUI -- a unica familia que nao pergunta nada ao mundo:
///      duas sementes literais deste arquivo, e o universo que sai de cada uma.
/// ==================================================================================================
///
/// ============================ ELA NUNCA TOCA NA PASTA DE VERDADE ============================
/// Tudo roda dentro do <see cref="NaCaixa"/> da bancada da limpeza -- o mesmo temporario, o mesmo
/// `finally` que devolve o `_store` e recarrega o mundo do dono do disco. A familia 4 chega a chamar
/// o `ExecutarLimpeza` DE PRODUCAO (e nao uma copia da ordem dele), e por isso ela so pode rodar
/// dentro da caixa: e uma limpeza total de verdade, contra um servidor de mentira.
/// ========================================================================================
///
///     Godot --headless -- --server --sementeteste
/// </summary>
public partial class GameServer
{
	private int _stOk, _stFalhou;

	private void AfirmarSt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _stOk++; GD.Print($"[semente]   OK    {oque}"); return; }
		_stFalhou++;
		GD.PrintErr($"[semente]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// A REGIAO DE ASSINATURA -- 8x8 celulas de sistema em volta da origem.
	///
	/// A assinatura e <see cref="Sistemas.Assinatura"/>, a MESMA que o verb `univ|` responde ao
	/// cliente: ela enumera cada corpo da regiao e mistura nome, posicao e raio. E o unico jeito
	/// honesto de dizer "e outro universo" -- comparar duas sementes provaria so que dois numeros sao
	/// diferentes, e nao que o mundo que sai deles e outro.
	/// </summary>
	private static ulong AssinaturaDoUniverso(ulong semente) => Sistemas.Assinatura(semente, -4, -4, 8);

	public void RodarBancadaDaSemente()
	{
		_stOk = _stFalhou = 0;
		GD.Print("[semente] ================ A SEMENTE DESTE UNIVERSO ================");

		if (_store == null)
		{
			AfirmarSt("o servidor tem pasta de saves", false, "sem AccountStore -- nada a medir");
			return;
		}

		GD.Print($"[semente] o mundo em que este servidor esta de pe: {SeedDoUniverso} "
			   + $"| assinatura {AssinaturaDoUniverso(SeedDoUniverso):X16}");

		NaCaixa("sementeteste", caixa =>
		{
			SecaoDoMundoNovo();
			SecaoDoReinicio();
			SecaoDoOutroUniverso();
			SecaoDoWipe(caixa);
			SecaoDoMundoVelho(caixa);
			SecaoDoFormato();
			SecaoDoDeterminismoComSementeFixa();
		});

		GD.Print($"[semente] ================ {_stOk} OK, {_stFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1. MUNDO NOVO SORTEIA
	// =====================================================================
	private void SecaoDoMundoNovo()
	{
		GD.Print("[semente] -- 1. um mundo novo sorteia o proprio endereco --");

		LimparACaixa();
		CarregarSemente();

		AfirmarSt("a semente do mundo novo nao e zero",
				  SeedDoUniverso != 0,
				  "zero e proibido: `RoboDeNav` usa `!= 0` como prova de que a semente chegou no login");
		AfirmarSt("...e ela NAO e a semente legada (o mundo novo nao herda o mundo antigo)",
				  SeedDoUniverso != SementeLegada, $"{SeedDoUniverso}");
		AfirmarSt("...e o universo.json existe (o endereco foi gravado, e nao so pensado)",
				  System.IO.File.Exists(CaminhoDaSemente));
		AfirmarSt("...e o servidor sabe que a memoria e o disco concordam", _sementeNoDisco);

		// E A SEMENTE E MESMO A RAIZ: a zona do espaco carrega ela dentro da chave. Se este par
		// batesse por acaso, a chave de espaco de dois universos seria a mesma -- e a nave de um
		// mundo pousaria no espaco do outro.
		AfirmarSt("a zona do espaco deste servidor deriva da semente deste mundo",
				  ZonaDoEspaco.Equals(Espaco.Zona(SeedDoUniverso)));
	}

	// =====================================================================
	// 2. REINICIAR NAO MUDA O MUNDO -- a lei
	// =====================================================================
	/// <summary>
	/// `CarregarSemente` E LITERALMENTE O QUE O BOOT FAZ (ele e chamado do `CarregarVisual`), entao
	/// chama-lo de novo E um reinicio, do ponto de vista deste sistema. Nao ha atalho aqui: a
	/// alternativa seria subir um segundo processo, que e o que a medicao de linha de comando faz --
	/// e as duas concordam.
	/// </summary>
	private void SecaoDoReinicio()
	{
		GD.Print("[semente] -- 2. reiniciar NAO muda o mundo --");

		ulong antes = SeedDoUniverso;
		ulong assinaturaAntes = AssinaturaDoUniverso(antes);
		ZoneKey espacoAntes = ZonaDoEspaco;

		bool sempreIgual = true, universoIgual = true;
		for (int i = 0; i < 5; i++)
		{
			CarregarSemente();
			if (SeedDoUniverso != antes) sempreIgual = false;
			if (AssinaturaDoUniverso(SeedDoUniverso) != assinaturaAntes) universoIgual = false;
		}

		AfirmarSt("cinco reinicios seguidos devolvem a MESMA semente", sempreIgual,
				  $"{antes} -> {SeedDoUniverso}");
		AfirmarSt("...e o MESMO universo (assinatura de 64 celulas de sistema)", universoIgual,
				  $"{assinaturaAntes:X16} -> {AssinaturaDoUniverso(SeedDoUniverso):X16}");
		AfirmarSt("...e a mesma zona de espaco (a chave nao muda de mao)",
				  ZonaDoEspaco.Equals(espacoAntes));
	}

	// =====================================================================
	// 3. OUTRO ENDERECO, OUTRO UNIVERSO -- a contraprova
	// =====================================================================
	/// <summary>
	/// SEM ESTA FAMILIA, A FAMILIA 2 NAO PROVA NADA. Um servidor que ignorasse a semente por completo
	/// -- que gerasse sempre o mesmo mapa cravado -- passaria em todas as checagens de "o mundo
	/// continua o mesmo" com nota cheia. E o mesmo cego que este projeto ja registrou: *"as duas telas
	/// concordam" fica verde com as duas erradas igual*.
	/// </summary>
	private void SecaoDoOutroUniverso()
	{
		GD.Print("[semente] -- 3. outro endereco tem que ser outro universo --");

		ulong antiga = SeedDoUniverso;
		ulong assinaturaAntiga = AssinaturaDoUniverso(antiga);

		// APAGAR O ARQUIVO E O QUE O WIPE FAZ NO DISCO -- aqui so o arquivo, pra medir uma coisa de
		// cada vez. A familia 4 mede a limpeza inteira.
		try { System.IO.File.Delete(CaminhoDaSemente); } catch { /* ja nao existe */ }
		CarregarSemente();

		AfirmarSt("sem o arquivo, o proximo mundo tem OUTRA semente", SeedDoUniverso != antiga,
				  $"{antiga} -> {SeedDoUniverso}");
		AfirmarSt("...e o universo que sai dela e OUTRO (a assinatura muda)",
				  AssinaturaDoUniverso(SeedDoUniverso) != assinaturaAntiga,
				  $"{assinaturaAntiga:X16} -> {AssinaturaDoUniverso(SeedDoUniverso):X16}");
		AfirmarSt("...e a zona do espaco tambem muda (ela carrega a semente na chave)",
				  !ZonaDoEspaco.Equals(Espaco.Zona(antiga)));

		// E O SORTEIO NAO REPETE. Oito sementes seguidas tem que dar oito valores distintos -- um
		// sorteio semeado pelo relogio daria repetido em maquina rapida, que e o defeito de novo
		// (mundos diferentes nascendo iguais) so que muito mais dificil de ver.
		var vistas = new HashSet<ulong>();
		for (int i = 0; i < 8; i++) vistas.Add(SortearSemente());
		AfirmarSt("oito sorteios dao oito sementes distintas", vistas.Count == 8, $"{vistas.Count} distintas");
		AfirmarSt("...e nenhuma delas e zero", !vistas.Contains(0));
	}

	// =====================================================================
	// 4. O WIPE ALCANCA A SEMENTE -- a familia do pedido
	// =====================================================================
	/// <summary>
	/// AS TRES COISAS QUE O WIPE TEM QUE FAZER COM A SEMENTE, e nenhuma delas cobre as outras:
	///   * o ARQUIVO some (senao o proximo boot volta pro mesmo universo);
	///   * a MEMORIA ja nasce noutro universo (senao o admin limpa o servidor, continua andando no
	///     mundo velho, e o primeiro cidadao do mundo "novo" nasce com a semente do antigo -- o
	///     *"disco limpo com cache sujo"* do cabecalho de `GameServer.Limpeza.cs`);
	///   * o endereco NOVO e GRAVADO logo depois (senao o boot seguinte le "pasta vazia" e sorteia um
	///     TERCEIRO universo, e quem criou conta entre o wipe e o reinicio perde o chao).
	///
	/// A terceira e a producao (`AdminLimparServidor`), e por isso ela e refeita aqui na mesma ordem:
	/// `ExecutarLimpeza` primeiro, `SalvarSemente` depois. Inverter a ordem grava um arquivo que a
	/// vassoura apaga em seguida -- e esta familia ficaria vermelha na hora.
	/// </summary>
	private void SecaoDoWipe(string caixa)
	{
		GD.Print("[semente] -- 4. o wipe troca de universo (o pedido do dono) --");

		LimparACaixa();
		CarregarSemente();

		// UM MUNDO COM GENTE DENTRO, pra a limpeza ter o que limpar alem da semente.
		System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");

		ulong antes = SeedDoUniverso;
		ulong assinaturaAntes = AssinaturaDoUniverso(antes);
		AfirmarSt("antes do wipe, o mundo tem endereco gravado",
				  System.IO.File.Exists(CaminhoDaSemente) && _sementeNoDisco);

		// ---------------------------------------------------------- a limpeza DE PRODUCAO
		ResultadoDaLimpeza r = ExecutarLimpeza(null);
		AfirmarSt("a limpeza nao deu erro de disco", r.Erros.Count == 0, string.Join("; ", r.Erros));

		AfirmarSt("o wipe APAGOU o universo.json (a vassoura alcanca a semente)",
				  !System.IO.File.Exists(CaminhoDaSemente),
				  "se sobrar, o wipe nao troca o mundo e o pedido nao foi cumprido");
		AfirmarSt("...e a MEMORIA ja esta noutro universo (nao ha cache sujo)",
				  SeedDoUniverso != antes, $"{antes} -> {SeedDoUniverso}");
		AfirmarSt("...e o servidor sabe que ainda nao gravou este endereco", !_sementeNoDisco);
		AfirmarSt("...e o mundo novo e MESMO outro mundo (a assinatura mudou)",
				  AssinaturaDoUniverso(SeedDoUniverso) != assinaturaAntes,
				  $"{assinaturaAntes:X16} -> {AssinaturaDoUniverso(SeedDoUniverso):X16}");

		// ---------------------------------------------------------- o que a producao faz em seguida
		ulong novo = SeedDoUniverso;
		SalvarSemente();
		AfirmarSt("a producao grava o endereco do mundo novo logo depois da vassoura",
				  System.IO.File.Exists(CaminhoDaSemente) && _sementeNoDisco);

		CarregarSemente();
		AfirmarSt("...e o proximo boot entra NO MUNDO NOVO, e nao num terceiro",
				  SeedDoUniverso == novo, $"{novo} -> {SeedDoUniverso}");
	}

	// =====================================================================
	// 5. MUNDO VELHO NAO TROCA DE SEMENTE
	// =====================================================================
	/// <summary>
	/// O SAVE DO DONO NASCEU ANTES DESTE SISTEMA. Ele tem contas, obras, dominios e planetas mortos, e
	/// nao tem `universo.json` -- e cada uma dessas coisas guarda o endereco de uma zona por dentro
	/// (`CharacterSave.ZonaSeed`, `Obra.ZonaSeed`, `Dominio(Sx, Sy, K)`). Sortear uma semente pra ele
	/// no primeiro boot seria mudar o mundo dele debaixo dele: quem deslogou no espaco voltaria pra
	/// uma zona que nao existe, e a casa que alguem ergueu ficaria de pe num planeta que nunca houve.
	///
	/// A segunda metade da familia e a que faz a primeira nao virar uma armadilha: o `admin.log` NAO
	/// conta como mundo velho. Ele e o unico arquivo que atravessa a limpeza -- conta-lo faria todo
	/// mundo pos-wipe ser lido como "antigo", e o wipe deixaria de trocar de universo.
	/// </summary>
	private void SecaoDoMundoVelho(string caixa)
	{
		GD.Print("[semente] -- 5. um mundo que JA EXISTE mantem a semente de sempre --");

		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");
		CarregarSemente();

		AfirmarSt("save antigo SEM universo.json fica com a semente de sempre",
				  SeedDoUniverso == SementeLegada, $"{SeedDoUniverso} (esperado {SementeLegada})");
		AfirmarSt("...e o endereco passa a existir, pra a pergunta nao ser feita de novo",
				  System.IO.File.Exists(CaminhoDaSemente) && _sementeNoDisco);

		// ---------------------------------------------------------- e a pasta pos-wipe NAO e "velha"
		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "admin.log"), "a testemunha\n");
		CarregarSemente();

		AfirmarSt("pasta so com o admin.log e MUNDO NOVO (o log nao conta como mundo)",
				  SeedDoUniverso != SementeLegada, $"{SeedDoUniverso}");

		// ---------------------------------------------------------- e o arquivo ilegivel PRESERVA
		// O custo do erro aqui e o mundo do dono, entao um `universo.json` corrompido numa pasta
		// povoada nao pode virar um mundo novo por conta propria.
		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");
		System.IO.File.WriteAllText(CaminhoDaSemente, "isto nao e json");
		CarregarSemente();

		AfirmarSt("universo.json ilegivel num mundo povoado NAO sorteia (preserva)",
				  SeedDoUniverso == SementeLegada, $"{SeedDoUniverso}");
	}

	// =====================================================================
	// 6. O FORMATO -- largura fixa
	// =====================================================================
	/// <summary>
	/// POR QUE UMA BANCADA PRA UM DETALHE DE FORMATO: a `--wipeteste` compara o mundo por RETRATO, e o
	/// retrato de disco dela e `nome (tamanho em bytes)`. Uma semente escrita em decimal muda de
	/// comprimento com o valor, entao o retrato do "primeiro boot" e o do "depois da limpeza" ficariam
	/// diferentes **pelo numero de digitos** -- e a bancada da limpeza reprovaria por um motivo que nao
	/// tem nada a ver com o que ela mede. Esta familia e o que impede alguem de "simplificar" o hexa.
	/// </summary>
	private void SecaoDoFormato()
	{
		GD.Print("[semente] -- 6. o formato: largura fixa (o retrato da --wipeteste depende disso) --");

		AfirmarSt("o hexa tem largura fixa (0x + 16)", Hexa(1).Length == 18 && Hexa(ulong.MaxValue).Length == 18,
				  $"{Hexa(1)} / {Hexa(ulong.MaxValue)}");
		AfirmarSt("...e a ida e volta preserva o valor",
				  LerHexa(Hexa(20260802)) == 20260802 && LerHexa(Hexa(ulong.MaxValue)) == ulong.MaxValue);
		AfirmarSt("...e le tambem sem o prefixo, pra quem editar o arquivo na mao",
				  LerHexA_SemPrefixo());

		LimparACaixa();
		_semente = 1;
		SalvarSemente();
		long tam1 = new System.IO.FileInfo(CaminhoDaSemente).Length;

		_semente = ulong.MaxValue;
		SalvarSemente();
		long tam2 = new System.IO.FileInfo(CaminhoDaSemente).Length;

		AfirmarSt("o universo.json tem o MESMO tamanho pra qualquer semente", tam1 == tam2,
				  $"{tam1} b contra {tam2} b -- o retrato da --wipeteste compara tamanho");
	}

	/// <summary>
	/// Separada so pra a linha da afirmacao caber: `LerHexa` aceita com e sem `0x`.
	///
	/// O LITERAL SAI DO `Hexa`, e nao da minha cabeca: a primeira versao desta linha tinha
	/// `0000000001352E7A` escrito a mao -- que nao e 20260802 -- e a bancada ficou vermelha na
	/// primeira rodada. Uma constante hexa conferida por leitura e uma constante hexa errada.
	/// </summary>
	private static bool LerHexA_SemPrefixo() => LerHexa(Hexa(20260802)[2..]) == 20260802;

	// =====================================================================
	// 7. DETERMINISMO, COM A SEMENTE ESCRITA AQUI
	// =====================================================================
	/// <summary>
	/// A UNICA FAMILIA QUE NAO PERGUNTA NADA AO MUNDO -- e por isso ela e a que continua valendo
	/// depois de qualquer wipe. As duas sementes sao literais deste arquivo: uma bancada de
	/// determinismo pendurada na semente do servidor compararia duas coisas que ninguem escolheu, e
	/// ficaria verde por acidente no dia em que o mundo mudasse.
	/// </summary>
	private void SecaoDoDeterminismoComSementeFixa()
	{
		GD.Print("[semente] -- 7. a mesma semente da o mesmo mundo (semente escrita nesta bancada) --");

		const ulong universoA = 0xDB26_0814_0000_0001UL;
		const ulong universoB = 0xDB26_0814_0000_0002UL;

		AfirmarSt("a mesma semente da a mesma assinatura de universo",
				  AssinaturaDoUniverso(universoA) == AssinaturaDoUniverso(universoA),
				  $"{AssinaturaDoUniverso(universoA):X16}");
		AfirmarSt("...e duas sementes dao dois universos",
				  AssinaturaDoUniverso(universoA) != AssinaturaDoUniverso(universoB),
				  $"{AssinaturaDoUniverso(universoA):X16} contra {AssinaturaDoUniverso(universoB):X16}");
		AfirmarSt("...e a mesma semente da a mesma zona de espaco",
				  Espaco.Zona(universoA).Equals(Espaco.Zona(universoA)));
		AfirmarSt("...e duas sementes dao duas zonas de espaco",
				  !Espaco.Zona(universoA).Equals(Espaco.Zona(universoB)));

		// E O PLANETA PRE-FEITO NAO SE MOVE. A Terra e ancorada (`Espaco.PreFeitos`), entao ela
		// existe em TODO universo -- e e por isso que trocar de semente nao deixa ninguem sem casa.
		AfirmarSt("os planetas pre-feitos existem em qualquer universo (a Terra nao se muda)",
				  Espaco.PreFeitos().Any(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase)));
	}

	// =====================================================================
	// A FERRAMENTA
	// =====================================================================
	/// <summary>
	/// ESVAZIA A CAIXA -- e so a caixa. Ela e o temporario do <see cref="NaCaixa"/>; o `_store` ja
	/// esta apontando pra la quando qualquer familia daqui roda, e a pasta de verdade nunca e tocada.
	/// </summary>
	private void LimparACaixa()
	{
		foreach (string f in _store!.TodosOsArquivos().ToList())
			try { System.IO.File.Delete(f); } catch { /* outra sessao segurando: a familia diz */ }
		_sementeNoDisco = false;
	}
}
