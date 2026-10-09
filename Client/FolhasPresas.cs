using Godot;

namespace Jandirus.Client;

/// <summary>
/// A PORTA DE TODA `SpriteFrames` DO CLIENTE -- e ela SEGURA o que carrega, pelo processo inteiro.
///
/// ============================ O DEFEITO QUE ELA FECHA ============================
/// O cache de recursos do Godot guarda tambem os SUB-recursos de um `.tres`: cada quadro de uma folha e um
/// `AtlasTexture` com caminho proprio ("folha.tres::AtlasTexture_7"). Quando o C# pede um quadro
/// (`GetFrameTexture`), nasce um involucro C# dele, e o involucro conta como referencia. Se a FOLHA morre e o
/// quadro sobrevive so por esse involucro -- a miniatura da tela de criacao, o retrato de um slot, o icone da
/// mochila, a camada de um boneco que trocou de folha --, ele passa a depender do coletor do .NET: o coletor
/// leva o involucro, e ate a thread de finalizacao devolver a referencia o quadro continua vivo no cache, sem
/// dono. Um `ResourceLoader.Load` da mesma folha nessa janela ACHA o quadro e o reusa, o vinculo C# dele fica
/// com a alca nula, e dali em diante o motor loga
///
///     ERROR: System.InvalidOperationException: Handle is not initialized.
///        at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(...)
///
/// toda vez que a contagem daquele quadro vai de 1 pra 2 (GodotSharp 4.7.1: o ramo do incremento de
/// `CSharpLanguage::_instance_binding_reference_callback` nao confere `is_released()`). O desenho nao muda -- o
/// quadro reusado e valido, e o vinculo se refaz quando o C# pede o quadro de novo --, mas o erro vai pro log, e
/// num quadro que so e DESENHADO ele se repete a cada redesenho.
///
/// MEDIDO em 2026-10-08 pela `--diaginvolucro`, antes desta porta: a tela de criacao deixava 225 das 227
/// miniaturas dela nesse estado ao sair, e 2 de 35 rodadas da `--diagcarga` logaram o erro vestindo os
/// habitantes da entrada no mundo.
///
/// ============================ O REMEDIO: A FOLHA NAO MORRE ============================
/// Folha que nunca sai da memoria nunca deixa quadro orfao: enquanto ela vive e ELA quem segura os quadros, e a
/// alca do involucro de cada um continua forte. Entao toda folha carregada por aqui entra num dicionario
/// estatico e fica ate o processo acabar.
///
/// DEPOIS dela a mesma bancada conta 0 de 227 e nenhum erro. De carona, revestir um corpo com folha ja vista
/// deixa de voltar ao `ResourceLoader`: os oito corpos da bancada caem de 61 pra 2,2 ms.
///
/// O CUSTO e memoria que nao volta: a textura de cada folha que o jogo chegou a usar. MEDIDO nos arquivos
/// (2026-10-08): as 264 folhas do catalogo de aparencia somam 56 MB, e essas o `Aquecimento` ja prendia desde o
/// lobby (pra quem gasta um segundo nele); as outras 125 que o codigo e os dados do jogo citam somam mais 23 MB.
/// E esse o teto, pra um processo que encoste em todas -- a porta so estende a regra do aquecimento a quem
/// entra correndo e as folhas de fora do catalogo.
///
/// ============================ POR ISSO ELA TEM QUE SER A UNICA PORTA ============================
/// Uma folha carregada por fora (um `ResourceLoader.Load` solto) pode morrer, deixar um quadro orfao e ser
/// ressuscitada DEPOIS por aqui, na primeira vez em que alguem a pedir a esta porta. A `--diaginvolucro` le os
/// fontes do cliente e reprova a chamada solta.
///
/// SO A THREAD PRINCIPAL: o dicionario nao tem trava, e todo chamador e codigo de cena. (O `Aquecimento` carrega
/// em thread pelo `LoadThreadedRequest` e guarda o que recolhe na lista dele; ele nao passa por aqui. Quem pede
/// folha a thread de carga POR AQUI e o <see cref="Adiantar"/>, e o que ele pede so esta porta recolhe.)
/// ================================================================================================
/// </summary>
public static class FolhasPresas
{
	private static readonly Dictionary<string, SpriteFrames> _presas = new(StringComparer.Ordinal);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaginvolucro --involucrodefeito`): a porta carrega e NAO segura -- o jogo de
	/// antes, em que a folha morria com o ultimo dono dela e os quadros que o C# tinha pedido ficavam no cache so
	/// pelo involucro. A bancada tem que voltar a contar os zumbis e os erros. Sempre falso em jogo.
	/// </summary>
	public static bool SemSegurarDeTeste;

	/// <summary>Quantas folhas estao presas agora -- pra bancada e pro log.</summary>
	public static int Quantas => _presas.Count;

	/// <summary>
	/// QUANTO A THREAD PRINCIPAL JA ESPEROU POR FOLHAS NESTA PORTA, em ms, desde que o processo abriu: a soma do
	/// tempo passado dentro do `ResourceLoader` por cada folha que nao estava presa. Pra bancada (`--diagestouro
	/// --temas`): a diferenca entre dois instantes diz quanto um trecho do jogo parou a tela lendo folha do disco,
	/// que e o que um teto de quadro nao separa do resto do quadro.
	/// </summary>
	public static double EsperaDeTeste { get; private set; }

	/// <summary>As folhas pedidas a thread de carga e ainda nao recolhidas. Ver <see cref="Adiantar"/>.</summary>
	private static readonly HashSet<string> _noAr = new(StringComparer.Ordinal);

	/// <summary>
	/// ============================ PEDE ESTA FOLHA ANTES DA HORA, NUMA THREAD DE CARGA ============================
	/// Pra quem sabe que vai vestir uma folha daqui a pouco e ainda nao a tem. Quem chama e a cinematica de
	/// transformacao, que conhece a forma -- e portanto o penteado, o corpo proprio e as coladas dela -- segundos antes
	/// do beat que os veste (`Transformacao.AdiantarAsFolhasDaForma`). Lida na hora, cada folha dessas parava a thread
	/// principal por 6,5 a 9,9 ms no quadro mais visto da cena (medido em 2026-10-08, `--diagestouro --temas --cenas`).
	///
	/// O <see cref="Carregar"/> CONTINUA SENDO A UNICA ENTREGA. A folha pedida aqui fica "no ar" ate alguem a pedir
	/// pela porta, ou ate o <see cref="RecolherAsAdiantadas"/> a achar pronta; e quem a recolhe e o `LoadThreadedGet`,
	/// a chamada do Godot feita pra ESPERAR uma carga em andamento. Um `ResourceLoader.Load` cru do mesmo caminho com a
	/// thread ainda lendo e a corrida que ja travou este jogo uma vez (ver o cabecalho do `Aquecimento`) -- por isso o
	/// conjunto do que esta no ar mora AQUI, na porta por onde toda folha passa.
	///
	/// NAO PEDE O QUE JA ESTA NA MEMORIA -- presa aqui, ou no cache do Godot por outro dono (o `Aquecimento` traz as do
	/// catalogo de aparencia): pra essas o `Carregar` nao le disco nenhum.
	/// ==============================================================================================================
	/// </summary>
	public static void Adiantar(string caminho)
	{
		if (SemSegurarDeTeste || _presas.ContainsKey(caminho) || _noAr.Contains(caminho)) return;
		if (ResourceLoader.HasCached(caminho)) return;
		if (!ResourceLoader.Exists(caminho) || ResourceLoader.LoadThreadedRequest(caminho) != Error.Ok) return;
		_noAr.Add(caminho);
	}

	/// <summary>
	/// RECOLHE AS ADIANTADAS QUE A THREAD DE CARGA JA ENTREGOU, e so essas: `LoadThreadedGetStatus` e uma consulta, e o
	/// `LoadThreadedGet` de uma carga acabada e uma entrega. Chamado por quadro por quem adiantou: a folha passa a
	/// estar PRESA antes de alguem precisar dela, e nada fica pendurado na thread.
	/// </summary>
	public static void RecolherAsAdiantadas()
	{
		if (_noAr.Count == 0) return;

		List<string>? prontas = null;
		foreach (string caminho in _noAr)
			if (ResourceLoader.LoadThreadedGetStatus(caminho) != ResourceLoader.ThreadLoadStatus.InProgress)
				(prontas ??= []).Add(caminho);
		if (prontas == null) return;
		foreach (string caminho in prontas) Carregar(caminho);
	}

	/// <summary>
	/// A folha deste caminho. A primeira chamada carrega pelo `ResourceLoader`; as seguintes devolvem a mesma
	/// folha sem voltar ao motor.
	/// </summary>
	/// <returns>
	/// Nulo quando o `ResourceLoader` devolve nulo (o arquivo nao existe ou nao foi importado). A falha nao e
	/// guardada: a chamada seguinte tenta de novo, como sempre foi.
	/// </returns>
	public static SpriteFrames? Carregar(string caminho)
	{
		// PEDIDA A THREAD DE CARGA E AINDA NAO RECOLHIDA? Entao quem entrega e o `LoadThreadedGet`: com a leitura ja
		// acabada ele e uma entrega, e com ela em curso ele ESPERA por ela em vez de competir -- ver `Adiantar`.
		if (_noAr.Remove(caminho))
		{
			ulong pedida = Time.GetTicksUsec();
			// (`InvalidResource` = outro dono do mesmo pedido ja o recolheu: nao ha o que entregar)
			SpriteFrames? adiantada =
				ResourceLoader.LoadThreadedGetStatus(caminho) == ResourceLoader.ThreadLoadStatus.InvalidResource
					? null : ResourceLoader.LoadThreadedGet(caminho) as SpriteFrames;
			EsperaDeTeste += (Time.GetTicksUsec() - pedida) / 1000.0;
			if (adiantada != null)
			{
				_presas[caminho] = adiantada;
				return adiantada;
			}
			// (a carga falhou, ou outro a recolheu antes: segue pelo caminho de sempre)
		}

		if (SemSegurarDeTeste) return ResourceLoader.Load<SpriteFrames>(caminho);
		if (_presas.TryGetValue(caminho, out SpriteFrames? presa)) return presa;

		ulong t0 = Time.GetTicksUsec();
		SpriteFrames? folha = ResourceLoader.Load<SpriteFrames>(caminho);
		EsperaDeTeste += (Time.GetTicksUsec() - t0) / 1000.0;
		if (folha != null) _presas[caminho] = folha;
		return folha;
	}
}
