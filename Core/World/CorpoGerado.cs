namespace Jandirus.Core.World;

/// <summary>
/// DA ZONA DE VOLTA PRO CEU -- o corpo celeste de um mundo GERADO, achado a partir da `ZoneKey` dele.
///
/// ============================ POR QUE O CAMINHO DE VOLTA PRECISA EXISTIR ============================
/// O universo e funcao pura de `seed + celula` (<see cref="Sistemas.Do(ulong,int,int)"/>): do endereco
/// `(Sx, Sy, K)` sai o planeta inteiro -- nome, posicao, raio e a seed da superficie. O caminho de IDA
/// sempre existiu. O de VOLTA nao: quem tem so a zona (tipo + nome + seed, que e o que o save de um
/// personagem guarda) nao tinha como perguntar "onde fica este mundo?".
///
/// E a pergunta e de jogo, nao de curiosidade: o servidor so GERA um mundo sorteado sabendo o corpo
/// dele no espaco (e pra la que a decolagem devolve, e de la que sai o endereco de um dominio). Sem a
/// volta, um mundo gerado so nascia no servidor pelo POUSO vindo de orbita -- e quem LOGAVA parado num
/// deles ficava num chao sem colisao e de onde nao se decola. Ver `GameServer.MundoFrio.cs`.
/// ==================================================================================================
///
/// ============================ O ROTULO E UMA PISTA, E A SEED E A PROVA ============================
/// O nome de um mundo gerado e `"{bioma}-{|Sx| % 1000}{|Sy| % 1000}{k}"`
/// (<see cref="SistemaSolar.Planeta"/>). Ele NAO e chave -- perde o sinal da celula e cola os tres
/// numeros sem separador, entao "Deserto-120" sai de `[1:-2]k0` e de `[1:2]k0` (ver o cabecalho do
/// `Berco`). Mas ele reduz o universo infinito a um punhado de candidatos: o ultimo digito e a orbita
/// (sao no maximo <see cref="Sistemas.PlanetasMaximo"/> = 10, entao `k` tem UM digito), e o resto se
/// corta em dois numeros de 1 a 3 digitos, cada um com dois sinais.
///
/// Cada candidato e CONFERIDO: o sistema daquela celula e montado de verdade e o planeta da orbita `k`
/// tem que ter a MESMA seed e o MESMO nome da zona. A seed tem 64 bits -- ou a volta acha o corpo
/// exato, ou nao acha nenhum. Um rotulo que por acaso se pareca com o de outro mundo nao passa.
///
/// O ALCANCE e o do `% 1000` do rotulo: celulas ate 999 de distancia da origem em cada eixo. Uma
/// celula sao 65.536 px (quase 7 minutos de voo); a 999 delas da Terra ninguem chega.
///
/// SE O FORMATO DO ROTULO MUDAR, esta funcao passa a devolver nulo calada. Quem vigia e a bancada
/// `--gravidadeteste`: ela varre os mundos em volta da origem e exige a volta de CADA UM.
/// ==================================================================================================
/// </summary>
public static class CorpoGerado
{
	/// <summary>
	/// O corpo celeste desta zona e o endereco dele, ou nulo quando a zona nao e um mundo gerado DESTE
	/// universo (o espaco, um mapa feito a mao, um planeta de outra seed de mundo, uma zona de bancada).
	/// </summary>
	public static (PlanetaNoEspaco Corpo, int Sx, int Sy, int K)? DaZona(ulong seedDoMundo, ZoneKey zona)
	{
		if (zona.Kind != ZoneKey.KindProcedural || Espaco.EhEspaco(zona)) return null;

		int traco = zona.Name.LastIndexOf('-');
		if (traco < 0) return null;

		// DE 3 A 7 DIGITOS: um de cada celula no minimo, tres no maximo, e o da orbita.
		ReadOnlySpan<char> digitos = zona.Name.AsSpan(traco + 1);
		if (digitos.Length is < 3 or > 7) return null;
		foreach (char c in digitos)
			if (c is < '0' or > '9') return null;

		int k = digitos[^1] - '0';
		ReadOnlySpan<char> celulas = digitos[..^1];

		for (int corte = 1; corte < celulas.Length; corte++)
		{
			ReadOnlySpan<char> a = celulas[..corte], b = celulas[corte..];
			if (a.Length > 3 || b.Length > 3) continue;

			// INTEIRO NAO SE ESCREVE COM ZERO A ESQUERDA: "07" nao e o `ToString` de numero nenhum, entao
			// esse corte nao e o que montou o rotulo.
			if ((a.Length > 1 && a[0] == '0') || (b.Length > 1 && b[0] == '0')) continue;

			int ax = int.Parse(a), ay = int.Parse(b);

			// OS DOIS SINAIS DE CADA EIXO (o rotulo guarda o modulo). Zero so tem um.
			for (int sinalX = 1; sinalX >= -1; sinalX -= 2)
			{
				if (ax == 0 && sinalX < 0) continue;
				for (int sinalY = 1; sinalY >= -1; sinalY -= 2)
				{
					if (ay == 0 && sinalY < 0) continue;

					int sx = ax * sinalX, sy = ay * sinalY;
					if (Sistemas.Do(seedDoMundo, sx, sy) is not { } s) continue;
					if (k >= s.Orbitas || k == s.OrbitaPreFeita) continue;

					PlanetaNoEspaco p = s.Planeta(k);
					if (p.Seed == zona.Seed && string.Equals(p.Nome, zona.Name, StringComparison.Ordinal))
						return (p, sx, sy, k);
				}
			}
		}

		return null;
	}
}
